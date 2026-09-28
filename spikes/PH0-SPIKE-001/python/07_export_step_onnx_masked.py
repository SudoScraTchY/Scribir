"""PH0-SPIKE-001: mask-aware step graph (v2) export + validation.

v2 adds an `enc_mask` input [1, L+E] (int64, 0/1) applied through the model's
normal attention-mask path, so that offline sessions (whose processor mask zeroes
one tail padding frame) can be reproduced exactly.

Validates:
  1) streaming spot-checks (all-ones masks, multiple sizes) vs the torch wrapper;
  2) the full offline loop on the example audio vs the masked torch reference;
  3) saves JSON evidence.
"""
import os, sys, json, traceback
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
from torch import nn
import soundfile as sf
from transformers import AutoModelForAudioFrameClassification, AutoProcessor
from transformers.models.nemotron3_diarization.modeling_nemotron3_diarization import (
    Nemotron3DiarizationSpeakerCache,
)
import onnxruntime as ort

def log(*a):
    print(*a, flush=True)

proc = AutoProcessor.from_pretrained(MODELS)
model = AutoModelForAudioFrameClassification.from_pretrained(MODELS)
model.eval()
for p in model.parameters():
    p.requires_grad_(False)
log("torch:", torch.__version__, "| ort:", ort.__version__)

class StepWrapperV2(nn.Module):
    def __init__(self, m):
        super().__init__()
        self.m = m

    def forward(self, input_features, cached_embeds, enc_mask):
        m = self.m
        chunk_embeds = m.model.audio_tower.embedder(input_features)
        x = torch.cat([cached_embeds, chunk_embeds], dim=1)
        N = x.shape[1]
        position_ids = torch.arange(N, device=x.device)[None, :]
        tower = m.model.audio_tower(inputs_embeds=x, attention_mask=enc_mask.bool(), position_ids=position_ids)
        hidden = m.model.upsampler(m.model.proj(tower.last_hidden_state))
        return m.classifier(hidden)

wrapper = StepWrapperV2(model)
wrapper.eval()
wrapper.float()

proc.set_streaming_mode("low_latency")
Tm = proc.num_mel_frames_per_audio_chunk
feats = torch.zeros(1, Tm, 128, dtype=torch.float32)
cached = torch.zeros(1, 21, 512, dtype=torch.float32)
mask = torch.ones(1, 21 + Tm // 8, dtype=torch.int64)
with torch.inference_mode():
    ref = wrapper(feats, cached, mask)
log("wrapper v2 example out:", tuple(ref.shape))

path = os.path.join(ONNX_DIR, "nemotron3_stream_step_mask.onnx")
exported_with = None

def attempt(desc, fn):
    try:
        fn()
        return desc
    except Exception as e:
        log(f"[export FAIL] {desc}: {type(e).__name__}: {e}")
        log(traceback.format_exc()[-1200:])
        return None

def a1():
    from torch.export import Dim
    dyn = ({1: Dim("Tm")}, {1: Dim("L")}, {1: Dim("M")})
    torch.onnx.export(wrapper, (feats, cached, mask), path,
                      input_names=["input_features", "cached_embeds", "enc_mask"],
                      output_names=["chunk_logits"],
                      dynamo=True, dynamic_shapes=dyn, opset_version=18)

exported_with = attempt("dynamo+dynamic_shapes(tuple)", a1)
if not exported_with:
    def a2():
        from torch.export import Dim
        dyn = {"input_features": {1: Dim("Tm")}, "cached_embeds": {1: Dim("L")}, "enc_mask": {1: Dim("M")}}
        torch.onnx.export(wrapper, (feats, cached, mask), path,
                          input_names=["input_features", "cached_embeds", "enc_mask"],
                          output_names=["chunk_logits"],
                          dynamo=True, dynamic_shapes=dyn, opset_version=18)
    exported_with = attempt("dynamo+dynamic_shapes(dict)", a2)
if not exported_with:
    log("V2 EXPORT FAILED")
    sys.exit(2)
log(f"[export ok] v2 via {exported_with} -> {path} ({os.path.getsize(path)} bytes)")

sess = ort.InferenceSession(path, providers=["CPUExecutionProvider"])
log("onnx v2 inputs :", [(i.name, i.shape) for i in sess.get_inputs()])
log("onnx v2 outputs:", [(o.name, o.shape) for o in sess.get_outputs()])

results = []

def step_compare(L, Tm2, tag):
    rng = np.random.default_rng(99 + L + Tm2)
    f = torch.from_numpy((rng.standard_normal((1, Tm2, 128)) * 0.5).astype(np.float32))
    c = torch.from_numpy((rng.standard_normal((1, L, 512)) * 0.5).astype(np.float32)) if L > 0 else torch.zeros(1, 0, 512)
    m = torch.ones(1, L + (Tm2 + 7) // 8, dtype=torch.int64)
    with torch.inference_mode():
        r = wrapper(f, c, m).numpy()
    o = sess.run(["chunk_logits"], {"input_features": f.numpy(), "cached_embeds": c.numpy(),
                                    "enc_mask": m.numpy()})[0]
    d = float(np.abs(r - o).max())
    log(f"[{tag}] L={L} Tm={Tm2} -> torch {tuple(r.shape)} onnx {tuple(o.shape)} maxabs={d:.3e}")
    results.append({"tag": tag, "L": L, "Tm": Tm2, "max_abs_diff": d})
    return d

step_compare(0, Tm, "stream-small")
step_compare(37, Tm, "stream-mid")
step_compare(104, Tm, "stream-large")

# ---------- full offline validation with mask ----------
def load_audio_16k(p):
    data, sr = sf.read(p, dtype="float32", always_2d=True)
    data = data.mean(axis=1).astype(np.float32)
    if sr != 16000:
        t = np.arange(len(data), dtype=np.float64) / sr
        tn = np.arange(0, len(data) / sr, 1.0 / 16000)
        data = np.interp(tn, t, data).astype(np.float32)
    return data

audio = load_audio_16k(os.path.join(MODELS, "diarization_example.mp3"))
inputs_off = proc(audio, sampling_rate=16000)
mask_full = inputs_off["attention_mask"]
embed_mask = mask_full[:, :: model.config.audio_config.subsampling_factor].bool()
with torch.inference_mode():
    ref_logits = model(**inputs_off).logits
log("offline masked ref:", tuple(ref_logits.shape))

chunk_length = model.config.chunk_length
right = model.config.chunk_right_context
cache = Nemotron3DiarizationSpeakerCache(
    model.config.streaming_config,
    fifo_length=model.config.fifo_length,
    speaker_cache_update_period=model.config.speaker_cache_update_period,
)
feats_all = inputs_off["input_features"].contiguous()
Tm_total = feats_all.shape[1]
with torch.inference_mode():
    all_embeds = model.model.audio_tower.embedder(feats_all)
N = all_embeds.shape[1]

slices = []
for start in range(0, N, chunk_length):
    end = min(start + chunk_length, N)
    num_chunk_frames = end - start
    chunk_embeds = all_embeds[:, start:min(end + right, N)]
    cached = torch.cat([cache.embeds[:, :cache.num_cache_frames], cache.fifo[:, :cache.num_fifo_frames]], dim=1) \
        if cache.is_initialized else torch.zeros(1, 0, 512, dtype=torch.float32)
    L = cached.shape[1]
    chunk_input = torch.cat([cached, chunk_embeds], dim=1)
    chunk_mask = embed_mask[:, start:start + chunk_embeds.shape[1]]
    step_mask = torch.cat([chunk_mask.new_ones(1, L), chunk_mask], dim=1)

    feats_chunk = feats_all[:, start * 8:min(end + right, N) * 8]
    full = torch.from_numpy(sess.run(
        ["chunk_logits"],
        {"input_features": feats_chunk.numpy(),
         "cached_embeds": cached.numpy(),
         "enc_mask": step_mask.to(torch.int64).numpy()})[0])
    cache.update(chunk_input, full, model.silence_embeds.to(full.dtype), num_chunk_frames, mask=step_mask)
    slices.append(full[:, L * 8:(L + num_chunk_frames) * 8])

onnx_logits = torch.cat(slices, dim=1)[:, :Tm_total]
maxd = float((onnx_logits - ref_logits).abs().max())
log(f"OFFLINE v2 max abs diff vs masked torch: {maxd:.3e}")
segs_ref = proc.extract_speaker_dict(ref_logits, mask_full)[0]
segs_onnx = proc.extract_speaker_dict(onnx_logits, None)[0]
same = len(segs_ref) == len(segs_onnx) and all(
    x["Speaker"] == y["Speaker"] and abs(x["Start"] - y["Start"]) <= 0.055 and abs(x["End"] - y["End"]) <= 0.055
    for x, y in zip(segs_ref, segs_onnx))
log(f"offline segments: torch={len(segs_ref)} onnx={len(segs_onnx)} match={same}")

evidence = {
    "v2_onnx": path, "exported_with": exported_with,
    "stream_checks": results,
    "offline_max_abs_diff_vs_masked_ref": maxd,
    "offline_segments_torch": len(segs_ref), "offline_segments_onnx": len(segs_onnx),
    "offline_segments_match": bool(same),
}
with open(os.path.join(LOGS, "v2_mask_validation.json"), "w", encoding="utf-8") as fh:
    json.dump(evidence, fh, indent=2)
log("saved:", os.path.join(LOGS, "v2_mask_validation.json"))
log("V2 VALIDATION DONE")
