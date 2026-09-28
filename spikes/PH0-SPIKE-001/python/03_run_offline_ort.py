"""PH0-SPIKE-001: OFFLINE-configuration validation through the exported step graph.

Mirrors Nemotron3DiarizationForAudioFrameClassification.forward's offline branch:
  - the whole recording is split into chunks of config.chunk_length encoder frames
    (340) with up to config.chunk_right_context (40) look-ahead frames;
  - the cache uses fifo_length=config.fifo_length (40) and
    speaker_cache_update_period=config.speaker_cache_update_period (300);
  - returned logits are the per-chunk slices concatenated and trimmed to the total
    number of mel frames.

Reference: one plain transformers call (model(**inputs)) which does this internally.
ONNX path: the same loop driven externally over the exported step graph.
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
os.makedirs(LOGS, exist_ok=True)

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
ap.add_argument("--audio", default=os.path.join(MODELS, "diarization_example.mp3"))
ap.add_argument("--onnx", default=os.path.join(ONNX_DIR, "nemotron3_stream_step.onnx"))
ap.add_argument("--tag", default="example")
args = ap.parse_args()

proc = AutoProcessor.from_pretrained(MODELS)
model = AutoModelForAudioFrameClassification.from_pretrained(MODELS)
model.eval()
for p in model.parameters():
    p.requires_grad_(False)
sess = ort.InferenceSession(args.onnx, providers=["CPUExecutionProvider"])

def load_audio_16k(path):
    try:
        from transformers.audio_utils import load_audio
        a = load_audio(path, sampling_rate=16000)
        a = a.detach().cpu().numpy() if hasattr(a, "detach") else np.asarray(a)
        return a.astype(np.float32).reshape(-1)
    except Exception as e:
        log("load_audio failed:", repr(e), "- soundfile fallback")
        data, sr = sf.read(path, dtype="float32", always_2d=True)
        data = data.mean(axis=1).astype(np.float32)
        if sr != 16000:
            t = np.arange(len(data), dtype=np.float64) / sr
            tn = np.arange(0, len(data) / sr, 1.0 / 16000)
            data = np.interp(tn, t, data).astype(np.float32)
        return data

audio = load_audio_16k(args.audio)
log(f"audio: {args.audio}  {len(audio)} samples  {len(audio)/16000:.2f}s")

# ---- torch offline reference ----
inputs_off = proc(audio, sampling_rate=16000)
mask = inputs_off.get("attention_mask")
nz = int((mask[0] == 0).sum().item()) if mask is not None else 0
log(f"offline attention_mask zeros: {nz}")
embed_mask = mask[:, :: model.config.audio_config.subsampling_factor].bool() if mask is not None else None
with torch.inference_mode():
    out_off = model(**inputs_off)
    out_off_nomask = model(input_features=inputs_off["input_features"], attention_mask=None)
ref_logits = out_off.logits
ref_logits_nomask = out_off_nomask.logits
log("torch offline logits:", tuple(ref_logits.shape))

# ---- ONNX offline loop ----
chunk_length = model.config.chunk_length              # 340
right = model.config.chunk_right_context              # 40
cache = Nemotron3DiarizationSpeakerCache(
    model.config.streaming_config,
    fifo_length=model.config.fifo_length,             # 40
    speaker_cache_update_period=model.config.speaker_cache_update_period,  # 300
)

feats = inputs_off["input_features"].contiguous()
Tm_total = feats.shape[1]
with torch.inference_mode():
    all_embeds = model.model.audio_tower.embedder(feats)   # [1, N, 512]
N = all_embeds.shape[1]
log(f"total mel frames: {Tm_total}; total encoder frames: {N}")
num_chunks = int(np.ceil(N / chunk_length))
log(f"offline chunks: {num_chunks} (chunk_length={chunk_length}, right_context={right})")

slices = []
step_records = []
for start in range(0, N, chunk_length):
    end = min(start + chunk_length, N)
    num_chunk_frames = end - start
    chunk_embeds = all_embeds[:, start:min(end + right, N)]
    cached = torch.cat([cache.embeds[:, :cache.num_cache_frames], cache.fifo[:, :cache.num_fifo_frames]], dim=1) \
        if cache.is_initialized else torch.zeros(1, 0, 512, dtype=torch.float32)
    L = cached.shape[1]
    chunk_input = torch.cat([cached, chunk_embeds], dim=1)

    # run the exported graph on the actual mel features of this chunk region
    chunk_tm_start = start * 8
    chunk_tm_end = min(end + right, N) * 8
    feats_chunk = feats[:, chunk_tm_start:chunk_tm_end]
    full = torch.from_numpy(
        sess.run(["chunk_logits"], {"input_features": feats_chunk.numpy(),
                                    "cached_embeds": cached.numpy()})[0])
    sl = full[:, L * 8:(L + num_chunk_frames) * 8]
    step_mask = None
    if embed_mask is not None:
        chunk_mask = embed_mask[:, start:start + chunk_embeds.shape[1]]
        step_mask = torch.cat([chunk_mask.new_ones(1, cached.shape[1]), chunk_mask], dim=1)
    cache.update(chunk_input, full, model.silence_embeds.to(full.dtype), num_chunk_frames, mask=step_mask)
    slices.append(sl)
    step_records.append({"chunk_start": start, "chunk_frames": num_chunk_frames,
                         "L_before": int(L), "out_frames": int(sl.shape[1]),
                         "ncf_after": int(cache.num_cache_frames), "nff_after": int(cache.num_fifo_frames)})
    log(f"chunk {start:5d}: frames={num_chunk_frames} L={L} -> out={sl.shape[1]} "
        f"ncf={cache.num_cache_frames} nff={cache.num_fifo_frames}")

onnx_logits = torch.cat(slices, dim=1)[:, :Tm_total]
log("onnx offline logits (trimmed):", tuple(onnx_logits.shape))
assert onnx_logits.shape == ref_logits.shape, "shape mismatch vs reference"

maxd = float((onnx_logits - ref_logits).abs().max())
d_mask_effect = float((ref_logits - ref_logits_nomask).abs().max())
d_onnx_vs_nomask = float((onnx_logits - ref_logits_nomask).abs().max())
log(f"offline max abs diff (onnx vs torch masked)  : {maxd:.3e}")
log(f"offline max abs diff (torch masked vs nomask): {d_mask_effect:.3e}")
log(f"offline max abs diff (onnx vs torch nomask)  : {d_onnx_vs_nomask:.3e}")

segs_ref = proc.extract_speaker_dict(ref_logits, inputs_off.get("attention_mask"))[0]
segs_onnx = proc.extract_speaker_dict(onnx_logits, None)[0]
log(f"segments torch offline: {len(segs_ref)}")
log(f"segments onnx  offline: {len(segs_onnx)}")

def same(a, b, tol=0.055):
    if len(a) != len(b):
        return False
    return all(x["Speaker"] == y["Speaker"] and abs(x["Start"] - y["Start"]) <= tol
               and abs(x["End"] - y["End"]) <= tol for x, y in zip(a, b))

match = same(segs_ref, segs_onnx)
log("segments match:", match)

evidence = {"audio": args.audio, "tag": args.tag, "total_frames": int(Tm_total),
            "num_chunks": num_chunks, "max_abs_diff": maxd,
            "torch_mask_effect": d_mask_effect, "onnx_vs_nomask": d_onnx_vs_nomask,
            "mask_zeros": nz,
            "segments_torch": len(segs_ref), "segments_onnx": len(segs_onnx), "match": match,
            "segments_onnx_list": segs_onnx, "steps": step_records}
with open(os.path.join(LOGS, f"offline_validation_{args.tag}.json"), "w", encoding="utf-8") as fh:
    json.dump(evidence, fh, indent=2)
log("saved:", os.path.join(LOGS, f"offline_validation_{args.tag}.json"))
log("OFFLINE VALIDATION DONE")
