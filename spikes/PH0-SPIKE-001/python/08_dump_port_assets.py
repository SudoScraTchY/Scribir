"""PH0-SPIKE-001: dump port assets + reference vectors for a future C# integration port.

Writes binary tensors (little-endian float32) + a manifest describing shapes, plus
README.md. These allow a C# implementation of the mel frontend / embedder / graph
wiring to self-validate against bit-comparable reference vectors.
"""
import os, sys, json
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
OUT = os.path.join(ROOT, "artifacts", "csharp-port-assets")
os.makedirs(OUT, exist_ok=True)

import torch
import soundfile as sf
from transformers import AutoModelForAudioFrameClassification, AutoProcessor
from transformers.models.nemotron3_diarization.modeling_nemotron3_diarization import (
    Nemotron3DiarizationSpeakerCache,
)
import onnxruntime as ort

try:
    from transformers.models.nemotron_asr_streaming.feature_extraction_nemotron_asr_streaming import (
        LOG_ZERO_GUARD_VALUE,
    )
except Exception:
    LOG_ZERO_GUARD_VALUE = None

def log(*a):
    print(*a, flush=True)

def dump(name, arr):
    a = np.asarray(arr, dtype="<f4")
    a.tofile(os.path.join(OUT, name))
    return {"file": name, "shape": list(a.shape), "dtype": "float32-le", "bytes": int(a.size * 4)}

proc = AutoProcessor.from_pretrained(MODELS)
model = AutoModelForAudioFrameClassification.from_pretrained(MODELS)
model.eval()
for p in model.parameters():
    p.requires_grad_(False)
fe = proc.feature_extractor
sess = ort.InferenceSession(os.path.join(ONNX_DIR, "nemotron3_stream_step.onnx"),
                            providers=["CPUExecutionProvider"])

manifest = {"generated_by": "08_dump_port_assets.py", "constants": {}, "assets": [], "references": {}}

# --- constants / small tensors ---
window = torch.hann_window(fe.win_length, periodic=False).numpy()
manifest["assets"].append(dump("window_hann_400.bin", window))
mel_filters = fe.mel_filters.detach().cpu().numpy()
manifest["assets"].append(dump("mel_filters_128x257.bin", mel_filters))
silence = model.silence_embeds.detach().cpu().numpy()
manifest["assets"].append(dump("silence_embeds_512.bin", silence))
proj_w = model.model.audio_tower.embedder.projection.weight.detach().cpu().numpy()
manifest["assets"].append(dump("embedder_projection_weight_512x1024.bin", proj_w))

manifest["constants"] = {
    "sampling_rate": int(fe.sampling_rate),
    "n_fft": int(fe.n_fft),
    "win_length": int(fe.win_length),
    "hop_length": int(fe.hop_length),
    "feature_size": int(fe.feature_size),
    "preemphasis": float(fe.preemphasis),
    "log_zero_guard_value": (float(LOG_ZERO_GUARD_VALUE) if LOG_ZERO_GUARD_VALUE is not None else None),
    "subsampling_factor": int(proc.subsampling_factor),
    "mel_formula": "librosa.filters.mel(sr=16000, n_fft=512, n_mels=128, fmin=0, fmax=8000, norm='slaney'); power spectrum magnitudes; log(mel @ power + LOG_ZERO_GUARD)",
    "normalization": "none",
    "stft_pad_mode": "constant",
    "embedder": "pad mel frames to multiple of 8 (zeros), reshape [T/8, 1024], linear 1024->512 (no bias)",
}
log("constants:", json.dumps(manifest["constants"], indent=1))

# --- reference vectors ---
def load_audio_16k(p):
    data, sr = sf.read(p, dtype="float32", always_2d=True)
    data = data.mean(axis=1).astype(np.float32)
    return data

audio = load_audio_16k(os.path.join(ROOT, "artifacts", "audio", "two_speaker_fixture.wav"))
first = proc.num_samples_first_audio_chunk
per = proc.num_samples_per_audio_chunk
step = proc.num_mel_frames_per_step
right = proc._streaming_chunk_sizes[1]

def prepare_audio_for_chunk_idx(k):
    if k == 0:
        return audio[:first], True, False
    mel_idx = step * k
    start = proc.audio_chunk_start(mel_idx)
    end = start + per
    if end <= len(audio):
        return audio[start:end], False, False
    start2 = proc.audio_chunk_start(step * (k))  # same formula
    return audio[start:], False, True

cache = Nemotron3DiarizationSpeakerCache(model.config.streaming_config)
refs = {}
for k in (0, 40):
    samples, is_first, is_last = prepare_audio_for_chunk_idx(k)
    # advance cache to step k by replaying cheaply for 40 steps? For k=40 we need the cache state;
    # replay all steps up to k quickly (graph runs are fast).
    pass

# replay full run up to step 40, saving states for k=0 and k=40
saved = {}
cache = Nemotron3DiarizationSpeakerCache(model.config.streaming_config)
mel_idx = 0
for k in range(0, 41):
    if k == 0:
        samples = audio[:first]; is_first = True; is_last = False
    else:
        start = proc.audio_chunk_start(step * k)
        samples = audio[start:start + per]; is_first = False; is_last = False
    inputs = proc(samples, sampling_rate=16000, is_streaming=True,
                  is_first_audio_chunk=is_first, is_last_audio_chunk=is_last)
    feats = inputs["input_features"].contiguous()
    Tm = feats.shape[1]
    with torch.inference_mode():
        chunk_embeds = model.model.audio_tower.embedder(feats)
    cached = torch.cat([cache.embeds[:, :cache.num_cache_frames], cache.fifo[:, :cache.num_fifo_frames]], dim=1) \
        if cache.is_initialized else torch.zeros(1, 0, 512)
    L = cached.shape[1]
    full = torch.from_numpy(sess.run(["chunk_logits"],
                                     {"input_features": feats.numpy(), "cached_embeds": cached.numpy()})[0])
    E = chunk_embeds.shape[1]
    num_chunk_frames = (E - right) if not is_last else E
    sliced = full[:, L * 8:(L + num_chunk_frames) * 8][:, :Tm]
    if k in (0, 40):
        r = {"step": k, "audio_samples": int(len(samples)), "is_first": bool(is_first)}
        r["audio"] = dump(f"ref_step{k:02d}_audio.bin", samples)
        r["mel"] = dump(f"ref_step{k:02d}_mel_1xTmx128.bin", feats.numpy())
        r["chunk_embeds"] = dump(f"ref_step{k:02d}_chunk_embeds_1xEx512.bin", chunk_embeds.numpy())
        r["cached_in"] = dump(f"ref_step{k:02d}_cached_in_1xLx512.bin", cached.numpy())
        r["full_logits"] = dump(f"ref_step{k:02d}_full_logits.bin", full.numpy())
        r["sliced_logits"] = dump(f"ref_step{k:02d}_sliced_logits.bin", sliced.numpy())
        r["L"] = int(L); r["E"] = int(E); r["num_chunk_frames"] = int(num_chunk_frames)
        refs[f"step{k}"] = r
    chunk_input = torch.cat([cached, chunk_embeds], dim=1)
    cache.update(chunk_input, full, model.silence_embeds.to(full.dtype), num_chunk_frames, mask=None)
    if k == 40:
        refs["cache_after_step40"] = {
            "ncf": int(cache.num_cache_frames), "nff": int(cache.num_fifo_frames),
            "compressed": bool(cache.is_compressed),
            "embeds": dump("ref_cache_after40_embeds.bin", cache.embeds[:, :cache.num_cache_frames].numpy()),
            "probs": dump("ref_cache_after40_probs.bin", cache.probs[:, :cache.num_cache_frames].numpy()),
            "fifo": dump("ref_cache_after40_fifo.bin", cache.fifo[:, :cache.num_fifo_frames].numpy()),
        }

manifest["references"] = refs
with open(os.path.join(OUT, "manifest.json"), "w", encoding="utf-8") as fh:
    json.dump(manifest, fh, indent=2)

readme = """# PH0-SPIKE-001 C# port assets

Reference tensors + constants dumped from the Python pipeline (2026-09-29) so a future
C# integration port (mel frontend / embedder / cache) can self-validate.

Files are little-endian float32 tensors; shapes in `manifest.json`.

Key items:
- `window_hann_400.bin` — STFT window (`torch.hann_window(400, periodic=False)`).
- `mel_filters_128x257.bin` — mel filterbank [128, 257] (librosa slaney norm, fmin 0, fmax 8k).
- `embedder_projection_weight_512x1024.bin` — feature-stacking linear [512, 1024]; output = input @ W.T (no bias).
- `silence_embeds_512.bin` — learned silence embedding used by cache compression.
- `ref_step00_*` / `ref_step40_*` — for two real chunks of `two_speaker_fixture.wav`:
  raw audio, expected mel `[1, Tm, 128]`, expected chunk embeddings `[1, E, 512]`,
  the graph inputs (`cached_in`), full graph output and the sliced output the cache consumes.
- `ref_cache_after40_*` — speaker-cache tensors after step 40 (compressed path not yet reached).

Mel pipeline (see python/03 docs and transformers source `nemotron_asr_streaming`):
window 400, n_fft 512, hop 160, center=True for the first chunk / offline, center=False otherwise;
preemphasis 0.97 (x[t] - 0.97*x[t-1], first sample kept); power spectrum |stft|^2; mel = filters @ power;
features = log(mel + log_zero_guard); no normalization. Tail frames beyond valid lengths are zeroed.
"""
with open(os.path.join(OUT, "README.md"), "w", encoding="utf-8") as fh:
    fh.write(readme)

log("manifest written; assets:", [a["file"] for a in manifest["assets"]])
log("DONE")
