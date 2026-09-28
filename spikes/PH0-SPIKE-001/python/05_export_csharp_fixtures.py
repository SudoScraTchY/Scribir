"""PH0-SPIKE-001: export per-step fixtures for the C# ONNX Runtime replay runner.

Runs the full streaming pipeline in Python (ONNX Runtime + real cache class) on the
given audio and dumps, for every step:
  step_NNN_feats.bin          float32  [1, tm, 128]
  step_NNN_cached.bin         float32  [1, L, 512]
  step_NNN_logits.bin         float32  [1, out_frames, 8]   (sliced, as fed to the cache)
plus shapes.json with per-step shapes and the full concatenated logits
(all_logits.bin, [total_frames, 8]) and python_segments.json (threshold 0.5).
"""
import os, sys, json, argparse
import numpy as np

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
MODELS = os.path.join(ROOT, "artifacts", "models")
ONNX_DIR = os.path.join(ROOT, "artifacts", "onnx")
LOGS = os.path.join(HERE, "logs")

import torch
import soundfile as sf
from transformers import AutoModelForAudioFrameClassification, AutoProcessor
from transformers.models.nemotron3_diarization.modeling_nemotron3_diarization import (
    Nemotron3DiarizationSpeakerCache,
)
import onnxruntime as ort

def log(*a):
    print(*a, flush=True)

ap = argparse.ArgumentParser()
ap.add_argument("--audio", default=os.path.join(ROOT, "artifacts", "audio", "two_speaker_fixture.wav"))
ap.add_argument("--tag", default="fixture")
ap.add_argument("--onnx", default=os.path.join(ONNX_DIR, "nemotron3_stream_step.onnx"))
ap.add_argument("--mode", default="low_latency")
args = ap.parse_args()

outdir = os.path.join(ROOT, "artifacts", "csharp-fixtures", args.tag)
os.makedirs(outdir, exist_ok=True)

proc = AutoProcessor.from_pretrained(MODELS)
model = AutoModelForAudioFrameClassification.from_pretrained(MODELS)
model.eval()
for p in model.parameters():
    p.requires_grad_(False)
proc.set_streaming_mode(args.mode)
right = proc._streaming_chunk_sizes[1]
sess = ort.InferenceSession(args.onnx, providers=["CPUExecutionProvider"])

def load_audio_16k(path):
    data, sr = sf.read(path, dtype="float32", always_2d=True)
    data = data.mean(axis=1).astype(np.float32)
    if sr != 16000:
        t = np.arange(len(data), dtype=np.float64) / sr
        tn = np.arange(0, len(data) / sr, 1.0 / 16000)
        data = np.interp(tn, t, data).astype(np.float32)
    return data

audio = load_audio_16k(args.audio)
n = len(audio)
log(f"audio: {args.audio}  {n} samples  {n/16000:.2f}s")

first = proc.num_samples_first_audio_chunk
per = proc.num_samples_per_audio_chunk
step = proc.num_mel_frames_per_step

def gen_chunks():
    yield True, False, audio[:first]
    mel_idx = step
    start = proc.audio_chunk_start(mel_idx)
    while start + per <= n:
        yield False, False, audio[start:start + per]
        mel_idx += step
        start = proc.audio_chunk_start(mel_idx)
    if start < n:
        yield False, True, audio[start:]

chunks = list(gen_chunks())
log(f"chunks: {len(chunks)}")

cache = Nemotron3DiarizationSpeakerCache(model.config.streaming_config)
steps_meta = []
sliced_list = []
for i, (is_first, is_last, samples) in enumerate(chunks):
    inputs = proc(samples, sampling_rate=16000, is_streaming=True,
                  is_first_audio_chunk=is_first, is_last_audio_chunk=is_last)
    feats = inputs["input_features"].contiguous()
    Tm = feats.shape[1]
    with torch.inference_mode():
        chunk_embeds = model.model.audio_tower.embedder(feats)
    if cache.is_initialized:
        ncf, nff = cache.num_cache_frames, cache.num_fifo_frames
        cached = torch.cat([cache.embeds[:, :ncf], cache.fifo[:, :nff]], dim=1)
        L = ncf + nff
    else:
        cached = torch.zeros(1, 0, 512, dtype=torch.float32)
        L = 0
    full = torch.from_numpy(
        sess.run(["chunk_logits"], {"input_features": feats.numpy(), "cached_embeds": cached.numpy()})[0])
    E = chunk_embeds.shape[1]
    num_chunk_frames = (E - right) if not is_last else E
    sl = full[:, L * 8:(L + num_chunk_frames) * 8][:, :Tm].contiguous()
    chunk_input = torch.cat([cached, chunk_embeds], dim=1)
    cache.update(chunk_input, full, model.silence_embeds.to(full.dtype), num_chunk_frames, mask=None)

    feats.numpy().astype("<f4").tofile(os.path.join(outdir, f"step_{i:03d}_feats.bin"))
    cached.numpy().astype("<f4").tofile(os.path.join(outdir, f"step_{i:03d}_cached.bin"))
    sl.numpy().astype("<f4").tofile(os.path.join(outdir, f"step_{i:03d}_logits.bin"))
    steps_meta.append({"i": i, "tm": int(Tm), "L": int(L),
                       "out_frames": int(sl.shape[1]),
                       "ncf_after": int(cache.num_cache_frames),
                       "nff_after": int(cache.num_fifo_frames),
                       "compressed_after": bool(cache.is_compressed)})
    sliced_list.append(sl)
    if i % 20 == 0 or i == len(chunks) - 1:
        log(f"step {i:3d}: Tm={Tm} L={L} out={sl.shape[1]} ncf={cache.num_cache_frames} "
            f"nff={cache.num_fifo_frames} compressed={cache.is_compressed}")

all_logits = torch.cat(sliced_list, dim=1)  # [1, total, 8]
all_logits.squeeze(0).numpy().astype("<f4").tofile(os.path.join(outdir, "all_logits.bin"))
segs = proc.extract_speaker_dict(all_logits, threshold=0.5)[0]
with open(os.path.join(outdir, "python_segments.json"), "w", encoding="utf-8") as fh:
    json.dump({"threshold": 0.5, "total_frames": int(all_logits.shape[1]), "segments": segs}, fh, indent=2)
with open(os.path.join(outdir, "shapes.json"), "w", encoding="utf-8") as fh:
    json.dump({"audio": args.audio, "num_steps": len(chunks), "mode": args.mode,
               "total_frames": int(all_logits.shape[1]), "steps": steps_meta}, fh, indent=2)
log(f"segments (python): {len(segs)}")
for s in segs[:12]:
    log("   ", s)
log("fixtures written to:", outdir)
log("CSHARP FIXTURE EXPORT DONE")
