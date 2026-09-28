"""PH0-SPIKE-001: export ONE streaming step of nvidia/Nemotron-3-Diarization to ONNX.

Graph contract (Level 1, cache update stays host-side):
  inputs : input_features [1, Tm, 128] float32  (one chunk's mel, lookahead included)
           cached_embeds  [1, L, 512]  float32  (cat of speaker-cache + FIFO frames = encoder-input embeddings)
  output : chunk_logits  [1, (L + E)*8, 8] float32  (pre-slice; E = ceil(Tm/8) encoder frames)

Mirrors Nemotron3DiarizationForAudioFrameClassification.forward for the streaming single-chunk case:
  chunk_embeds = audio_tower.embedder(input_features)
  x = cat([cached_embeds, chunk_embeds], dim=1)
  tower = audio_tower(inputs_embeds=x, attention_mask=None, position_ids=arange(N))
  hidden = upsampler(proj(tower.last_hidden_state))
  chunk_logits = classifier(hidden)
"""
import os, sys, json, traceback
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
MODELS = os.path.join(ROOT, "artifacts", "models")
OUTDIR = os.path.join(ROOT, "artifacts", "onnx")
LOGS = os.path.join(HERE, "logs")
os.makedirs(OUTDIR, exist_ok=True)
os.makedirs(LOGS, exist_ok=True)

import torch
from torch import nn

def log(*a):
    print(*a, flush=True)

log("torch:", torch.__version__)
from transformers import AutoModelForAudioFrameClassification, AutoProcessor
import transformers
log("transformers:", transformers.__version__)

proc = AutoProcessor.from_pretrained(MODELS)
model = AutoModelForAudioFrameClassification.from_pretrained(MODELS)
model.eval()
for p in model.parameters():
    p.requires_grad_(False)
log("model loaded:", type(model).__name__)

class StepWrapper(nn.Module):
    def __init__(self, m):
        super().__init__()
        self.m = m

    def forward(self, input_features, cached_embeds):
        m = self.m
        chunk_embeds = m.model.audio_tower.embedder(input_features)
        x = torch.cat([cached_embeds, chunk_embeds], dim=1)
        N = x.shape[1]
        position_ids = torch.arange(N, device=x.device)[None, :]
        tower = m.model.audio_tower(inputs_embeds=x, attention_mask=None, position_ids=position_ids)
        hidden = m.model.upsampler(m.model.proj(tower.last_hidden_state))
        return m.classifier(hidden)

wrapper = StepWrapper(model)
wrapper.eval()
wrapper.float()

proc.set_streaming_mode("low_latency")
Tm = proc.num_mel_frames_per_audio_chunk
feats = torch.zeros(1, Tm, 128, dtype=torch.float32)
cached = torch.zeros(1, 21, 512, dtype=torch.float32)

with torch.inference_mode():
    ref = wrapper(feats, cached)
log("wrapper example out:", tuple(ref.shape))

path = os.path.join(OUTDIR, "nemotron3_stream_step.onnx")
exported_with = None

def attempt(desc, fn):
    try:
        fn()
        return desc
    except Exception as e:
        log(f"[export FAIL] {desc}: {type(e).__name__}: {e}")
        log(traceback.format_exc()[-1500:])
        return None

def try_all():
    # 1) dynamo=True, dynamic_shapes as tuple-of-dicts (positional args)
    def a1():
        from torch.export import Dim
        dyn = ({1: Dim("Tm")}, {1: Dim("L")})
        torch.onnx.export(wrapper, (feats, cached), path,
                          input_names=["input_features", "cached_embeds"],
                          output_names=["chunk_logits"],
                          dynamo=True, dynamic_shapes=dyn, opset_version=18)
    r = attempt("dynamo+dynamic_shapes(tuple)", a1)
    if r: return r
    # 2) dynamo=True, dynamic_shapes keyed by input name
    def a2():
        from torch.export import Dim
        dyn = {"input_features": {1: Dim("Tm")}, "cached_embeds": {1: Dim("L")}}
        torch.onnx.export(wrapper, (feats, cached), path,
                          input_names=["input_features", "cached_embeds"],
                          output_names=["chunk_logits"],
                          dynamo=True, dynamic_shapes=dyn, opset_version=18)
    r = attempt("dynamo+dynamic_shapes(dict)", a2)
    if r: return r
    # 3) legacy exporter with dynamic_axes
    def a3():
        torch.onnx.export(wrapper, (feats, cached), path,
                          input_names=["input_features", "cached_embeds"],
                          output_names=["chunk_logits"],
                          dynamic_axes={"input_features": {1: "Tm"},
                                        "cached_embeds": {1: "L"},
                                        "chunk_logits": {1: "NL"}},
                          opset_version=17, dynamo=False)
    r = attempt("legacy+dynamic_axes", a3)
    if r: return r
    # 4) jit.trace + legacy export
    def a4():
        traced = torch.jit.trace(wrapper, (feats, cached), strict=False)
        torch.onnx.export(traced, (feats, cached), path,
                          input_names=["input_features", "cached_embeds"],
                          output_names=["chunk_logits"],
                          dynamic_axes={"input_features": {1: "Tm"},
                                        "cached_embeds": {1: "L"},
                                        "chunk_logits": {1: "NL"}},
                          opset_version=17, dynamo=False)
    r = attempt("jit.trace+legacy", a4)
    return r

exported_with = try_all()
if not exported_with:
    log("ALL EXPORT ATTEMPTS FAILED")
    sys.exit(2)
log(f"[export ok] via {exported_with} -> {path} ({os.path.getsize(path)} bytes)")

# ---- verify in onnxruntime across multiple dynamic sizes ----
import onnxruntime as ort
log("onnxruntime:", ort.__version__)
sess = ort.InferenceSession(path, providers=["CPUExecutionProvider"])
log("onnx inputs :", [(i.name, i.shape, i.type) for i in sess.get_inputs()])
log("onnx outputs:", [(o.name, o.shape) for o in sess.get_outputs()])

results = []

def compare(L, Tm2):
    rng = np.random.default_rng(1234 + L + Tm2)
    f = torch.from_numpy((rng.standard_normal((1, Tm2, 128)) * 0.5).astype(np.float32))
    c = torch.from_numpy((rng.standard_normal((1, L, 512)) * 0.5).astype(np.float32)) if L > 0 else torch.zeros(1, 0, 512)
    with torch.inference_mode():
        r = wrapper(f, c).numpy()
    o = sess.run(["chunk_logits"], {"input_features": f.numpy(), "cached_embeds": c.numpy()})[0]
    d = float(np.abs(r - o).max())
    shape_ok = (r.shape == o.shape)
    log(f"L={L:4d} Tm={Tm2:4d} -> torch {tuple(r.shape)}  onnx {tuple(o.shape)}  maxabs={d:.3e}  shape_ok={shape_ok}")
    results.append({"L": L, "Tm": Tm2, "torch_shape": list(r.shape), "onnx_shape": list(o.shape), "max_abs_diff": d})
    return d

diffs = [
    compare(0, Tm),        # first chunk, empty cache
    compare(5, Tm),        # small cache
    compare(37, Tm),       # mid stream
    compare(104, Tm),      # cache + fifo full-ish
    compare(21, Tm - 3),   # last-chunk-like (shorter input)
]
maxd = max(diffs)
log(f"max abs diff overall: {maxd:.3e}")

meta = {
    "exported_with": exported_with,
    "onnx_path": path,
    "onnx_bytes": os.path.getsize(path),
    "torch": torch.__version__,
    "transformers": transformers.__version__,
    "onnxruntime": ort.__version__,
    "example_shapes": {"input_features": [1, Tm, 128], "cached_embeds": [1, 21, 512]},
    "checks": results,
    "max_abs_diff": maxd,
}
with open(os.path.join(LOGS, "export_report.json"), "w", encoding="utf-8") as fh:
    json.dump(meta, fh, indent=2)
log("saved:", os.path.join(LOGS, "export_report.json"))
log("EXPORT STAGE DONE")
