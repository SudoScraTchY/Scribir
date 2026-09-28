"""PH0-SPIKE-001: full streaming diarization via the exported ONNX step graph.

Two passes over the same chunks (mirror of the README streaming loop):
  A) pure transformers reference (real Nemotron3DiarizationSpeakerCache object)
  B) ONNX Runtime step graph + host-side cache updates (same real cache class)
Compares per-step sliced logits and cache state; produces segments; saves JSON evidence.
"""
import os, sys, json, argparse
import numpy as np

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

parser = argparse.ArgumentParser()
parser.add_argument("--audio", default=os.path.join(MODELS, "diarization_example.mp3"))
parser.add_argument("--mode", default="low_latency")
parser.add_argument("--onnx", default=os.path.join(ONNX_DIR, "nemotron3_stream_step.onnx"))
parser.add_argument("--tag", default="example")
args = parser.parse_args()

proc = AutoProcessor.from_pretrained(MODELS)
model = AutoModelForAudioFrameClassification.from_pretrained(MODELS)
model.eval()
for p in model.parameters():
    p.requires_grad_(False)
proc.set_streaming_mode(args.mode)
sess = ort.InferenceSession(args.onnx, providers=["CPUExecutionProvider"])
log("onnx session inputs:", [(i.name, i.shape) for i in sess.get_inputs()])

def load_audio_16k(path):
    try:
        from transformers.audio_utils import load_audio
        a = load_audio(path, sampling_rate=16000)
        a = a.detach().cpu().numpy() if hasattr(a, "detach") else np.asarray(a)
        return a.astype(np.float32).reshape(-1)
    except Exception as e:
        log("load_audio failed:", repr(e), "- falling back to soundfile")
        data, sr = sf.read(path, dtype="float32", always_2d=True)
        data = data.mean(axis=1).astype(np.float32)
        if sr != 16000:
            t = np.arange(len(data), dtype=np.float64) / sr
            tn = np.arange(0, len(data) / sr, 1.0 / 16000)
            data = np.interp(tn, t, data).astype(np.float32)
        return data

audio = load_audio_16k(args.audio)
n = len(audio)
log(f"audio: {args.audio}")
log(f"samples={n} seconds={n/16000:.2f}")

first = proc.num_samples_first_audio_chunk
per = proc.num_samples_per_audio_chunk
step = proc.num_mel_frames_per_step
right = proc._streaming_chunk_sizes[1]
log(f"chunk geometry: first={first} per={per} mel/step={step} lookahead_enc_frames={right}")

assert n >= first, "audio shorter than first chunk"
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
log(f"num chunks: {len(chunks)}")

def prep_inputs(samples, is_first, is_last):
    return proc(samples, sampling_rate=16000, is_streaming=True,
                is_first_audio_chunk=is_first, is_last_audio_chunk=is_last)

def fwd_kwargs(inputs):
    kw = {}
    if "num_lookahead_frames" in inputs:
        kw["num_lookahead_frames"] = int(inputs["num_lookahead_frames"])
    return kw

def cache_vec(cache):
    if cache is None or not cache.is_initialized:
        return None
    ncf, nff = cache.num_cache_frames, cache.num_fifo_frames
    return (cache.embeds[:, :ncf].detach().clone(),
            cache.probs[:, :ncf].detach().clone(),
            cache.fifo[:, :nff].detach().clone())

def get_cached_tensor(cache):
    if cache is None or not cache.is_initialized:
        return torch.zeros(1, 0, 512, dtype=torch.float32)
    ncf, nff = cache.num_cache_frames, cache.num_fifo_frames
    return torch.cat([cache.embeds[:, :ncf], cache.fifo[:, :nff]], dim=1)

# ---------------- pass A: torch reference ----------------
recA = []
cache = None
logitsA = []
for i, (is_first, is_last, samples) in enumerate(chunks):
    inputs = prep_inputs(samples, is_first, is_last)
    feats = inputs["input_features"]
    assert bool(inputs["attention_mask"].all()), f"chunk {i}: attention_mask not all ones"
    with torch.inference_mode():
        out = model(input_features=feats, attention_mask=inputs["attention_mask"],
                    speaker_cache=cache, **fwd_kwargs(inputs))
    cache = out.speaker_cache
    Lb = 0 if (i == 0) else recA[-1]["cache_len_after"]
    recA.append({
        "i": i, "L_before": Lb,
        "logits_shape": list(out.logits.shape),
        "cache_len_after": int(cache.num_cache_frames + cache.num_fifo_frames),
        "ncf": int(cache.num_cache_frames), "nff": int(cache.num_fifo_frames),
        "compressed": bool(cache.is_compressed),
    })
    logitsA.append(out.logits.clone())
    recA[-1]["state"] = cache_vec(cache)

log("pass A (torch) done:",
    f"{len(logitsA)} chunk outputs; total frames={sum(t.shape[1] for t in logitsA)}")

# ---------------- pass B: onnx + host cache ----------------
recB = []
logitsB = []
cache = Nemotron3DiarizationSpeakerCache(model.config.streaming_config)
for i, (is_first, is_last, samples) in enumerate(chunks):
    inputs = prep_inputs(samples, is_first, is_last)
    feats = inputs["input_features"]
    Tm = feats.shape[1]
    with torch.inference_mode():
        chunk_embeds = model.model.audio_tower.embedder(feats)
    cached = get_cached_tensor(cache)
    L = cached.shape[1]
    full = torch.from_numpy(
        sess.run(["chunk_logits"],
                 {"input_features": feats.numpy(), "cached_embeds": cached.numpy()})[0]
    )
    E = chunk_embeds.shape[1]
    num_chunk_frames = (E - right) if not is_last else E
    sliced = full[:, L * 8:(L + num_chunk_frames) * 8][:, :Tm]
    chunk_input_embeds = torch.cat([cached, chunk_embeds], dim=1)
    cache.update(chunk_input_embeds, full, model.silence_embeds.to(full.dtype),
                 num_chunk_frames, mask=None)
    logitsB.append(sliced.clone())
    recB.append({
        "i": i, "L_before": int(L),
        "logits_shape": list(sliced.shape),
        "cache_len_after": int(cache.num_cache_frames + cache.num_fifo_frames),
        "ncf": int(cache.num_cache_frames), "nff": int(cache.num_fifo_frames),
        "compressed": bool(cache.is_compressed),
        "state": cache_vec(cache),
    })

log("pass B (onnx) done:",
    f"{len(logitsB)} chunk outputs; total frames={sum(t.shape[1] for t in logitsB)}")

# ---------------- compare ----------------
steps = []
max_logits = 0.0
max_embeds = max_probs = max_fifo = 0.0
for a, b in zip(recA, recB):
    dl = float((logitsA[a["i"]] - logitsB[b["i"]]).abs().max())
    max_logits = max(max_logits, dl)
    de = dp = df = 0.0
    sa, sb = a["state"], b["state"]
    if sa is not None and sb is not None:
        de = float((sa[0] - sb[0]).abs().max()) if sa[0].numel() else 0.0
        dp = float((sa[1] - sb[1]).abs().max()) if sa[1].numel() else 0.0
        df = float((sa[2] - sb[2]).abs().max()) if sa[2].numel() else 0.0
    max_embeds = max(max_embeds, de); max_probs = max(max_probs, dp); max_fifo = max(max_fifo, df)
    steps.append({
        "i": a["i"], "L_before": a["L_before"],
        "logits_diff": dl, "embeds_diff": de, "probs_diff": dp, "fifo_diff": df,
        "ncfA": a["ncf"], "nffA": a["nff"], "ncfB": b["ncf"], "nffB": b["nff"],
        "compressedA": a["compressed"], "compressedB": b["compressed"],
    })
    if a["i"] % 10 == 0 or a["i"] == len(recA) - 1:
        log(f"step {a['i']:3d}: L_before={a['L_before']:3d} dlogits={dl:.2e} "
            f"dembeds={de:.2e} dprobs={dp:.2e} dfifo={df:.2e} "
            f"ncf/nff A={a['ncf']}/{a['nff']} B={b['ncf']}/{b['nff']}")

log(f"MAX per-step diffs: logits={max_logits:.3e} embeds={max_embeds:.3e} "
    f"probs={max_probs:.3e} fifo={max_fifo:.3e}")

# ---------------- segments ----------------
catA = torch.cat(logitsA, dim=1)
catB = torch.cat(logitsB, dim=1)
segsA = proc.extract_speaker_dict(catA, threshold=0.5)[0]
segsB = proc.extract_speaker_dict(catB, threshold=0.5)[0]
log(f"segments (torch streaming): {len(segsA)}")
for s in segsA[:12]:
    log("   ", s)
log(f"segments (onnx streaming): {len(segsB)}")
for s in segsB[:12]:
    log("   ", s)

# offline torch reference (for shape of comparison; not ORT this pass)
inputs_off = proc(audio, sampling_rate=16000)
with torch.inference_mode():
    out_off = model(**inputs_off)
segsOff = proc.extract_speaker_dict(out_off.logits, inputs_off.get("attention_mask"))[0]
log(f"segments (torch offline): {len(segsOff)}")

def same_segments(a, b):
    if len(a) != len(b):
        return False
    for x, y in zip(a, b):
        if x["Speaker"] != y["Speaker"] or abs(x["Start"] - y["Start"]) > 0.055 or abs(x["End"] - y["End"]) > 0.055:
            return False
    return True

log("onnx vs torch streaming segments identical-ish:", same_segments(segsA, segsB))

evidence = {
    "audio": args.audio, "tag": args.tag, "mode": args.mode,
    "num_chunks": len(chunks),
    "max_logits_diff": max_logits, "max_embeds_diff": max_embeds,
    "max_probs_diff": max_probs, "max_fifo_diff": max_fifo,
    "segments_streaming_torch": len(segsA), "segments_streaming_onnx": len(segsB),
    "segments_offline_torch": len(segsOff),
    "segments_match_streaming": same_segments(segsA, segsB),
    "segments_onnx": segsB,
    "segments_offline": segsOff,
    "steps": steps,
}
out_path = os.path.join(LOGS, f"streaming_validation_{args.tag}.json")
with open(out_path, "w", encoding="utf-8") as fh:
    json.dump(evidence, fh, indent=2)
log("saved:", out_path)
log("STREAMING VALIDATION DONE")
