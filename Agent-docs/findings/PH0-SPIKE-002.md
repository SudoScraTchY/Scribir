# PH0-SPIKE-002 — Findings: end-to-end desktop proof (diarize → ASR → naive align)

**Status:** Done (spike). **Date:** 2026-09-29 · **Agent:** AutoCoder · **Task:** `PH0-SPIKE-002`.
**Machine:** Windows 10 x64 (CPU-only runs; GTX 1660 unused by design).
**Depends on:** PH0-SPIKE-001 (runtime recommendation: raw ONNX Runtime for the diarizer; sherpa-onnx for ASR).

---

## TL;DR

The full desktop pipeline shape works, end to end, in C#:

1. **Diarization** — raw `Microsoft.ML.OnnxRuntime` with a **native C# port** of the validated PH0-SPIKE-001
   pipeline (mel frontend + feature-stacking embedder + AOSC speaker cache + segment extraction).
   Validated against the Python reference: **all gates pass**; the end-to-end ports reproduce the
   reference segments **exactly** (8/8 on the ground-truth fixture).
2. **ASR** — sherpa-onnx 1.13.8 with **Whisper small int8, multilingual** (the small/quantized model
   requirement). Two constraints discovered and handled: the offline decode window is **<30 s** per call,
   and token timestamps require a **cross-attention-enabled decoder** (stock exports lack it).
3. **Naive alignment** — two working modes: *per-turn* transcription (cut at diarization turns; turns
   >28 s split) for long audio; **token-timestamp utterances** with max-overlap speaker assignment when
   the attention-enabled model is used (≤28 s files). The attention-enabled export was produced on this
   machine (with a documented exporter fix) and demonstrated working.

Four English files processed; transcripts are coherent with correct speaker-turn structure.

---

## 1. Deliverables

| Artifact | Path |
|---|---|
| Diarizer port (C#) + reference gates | `spikes/PH0-SPIKE-002/csharp/DiarizerPort/` (`--check`; report under `runs/check/`) |
| End-to-end console (diarize→ASR→align) | `spikes/PH0-SPIKE-002/csharp/SpeakerTranscript/` (transcripts under `runs/<audio>/`) |
| Spike README (layout + reproduce) | `spikes/PH0-SPIKE-002/README.md` |
| Test audio (git-ignored) | `artifacts/audio/` — two k2-fsa conversation clips, synthetic fixture, NVIDIA 8-voice demo |
| Whisper models (git-ignored) | `artifacts/whisper/sherpa-onnx-whisper-small/` (stock) · `artifacts/whisper-export/` (attention-enabled, built here) |
| Full logs / JSON | `csharp/DiarizerPort/runs/`, `csharp/SpeakerTranscript/runs/` |

## 2. Validation evidence

### 2.1 Diarizer port gates (`DiarizerPort --check` → ALL CHECKS PASSED)

| Gate | Meaning | Result |
|---|---|---|
| 1a/1b | mel vs reference (center / no-center) | 1.76e-4 / 1.82e-4 (float32 noise floor; see 2.2) |
| 2a/2b | embedder vs reference | 9.81e-5 / 8.50e-5 |
| 3a/3b | cached_embeds replay over the full 72-step stream (pre/post first compression) | 1.446e-4 (tolerance 5e-4) |
| 3c | graph sliced logits vs fixture (same-runtime) | **0.0** |
| 4 | end-to-end segments vs Python reference on the fixture | **8 = 8, identical (within 20 ms)** |

### 2.2 The residual is torch float32 noise, not a logic error

A **float64** recomputation in numpy reproduces the torch reference within the *same*
magnitude that the C# port shows: mel 1.58e-4 (float64) vs 1.50e-4 (float32 numpy);
embedder 9.81e-5 (float64) vs 1.15e-4 (float32 numpy). I.e. the drift lives in the torch-side
float32 accumulation; the C# port is (super-)accurate and segment-equivalent.

### 2.3 sherpa-onnx constraints (captured verbatim)

- Offline Whisper decode: `"Only waves less than 30 seconds are supported. We process only the first 30
  seconds and discard the remaining data"` → long audio is transcribed **per diarization turn** (turns
  >28 s are split); this is also the natural "naive align" architecture for meetings.
- Stock decoders: `"enable_token_timestamps=true but the decoder model does not have cross-attention
  outputs. Timestamps will not be available. To enable timestamps, export the model with attention
  outputs using: python scripts/whisper/export-onnx-with-attention.py"`.

### 2.4 Attention-enabled Whisper export (achieved, with a fix)

- Ran `scripts/whisper/export-onnx-with-attention.py --model small` (openai-whisper 20250625, torch 2.14).
- **Default (dynamo) export produces graphs sherpa rejects**: `Reshape` errors
  (`input {1,4,768} → requested {768}`) at first decode — for both fp32 and int8 variants.
- **Fix: force the legacy exporter** (`dynamo=False`; still available in torch 2.14). The re-exported
  models work: `"token timestamps available"`, and the token-timestamp alignment path produces clean
  utterances (see 3.1). Patch (applied locally to the script copy):
  ```python
  _orig_onnx_export = torch.onnx.export
  def _legacy_onnx_export(*args, **kwargs):
      kwargs.setdefault('dynamo', False)
      return _orig_onnx_export(*args, **kwargs)
  torch.onnx.export = _legacy_onnx_export
  ```

### 2.5 ONNX Runtime coexistence (one process, two stacks)

The app pins ONNX Runtime **1.30.0** (Microsoft.ML.OnnxRuntime) over the version sherpa bundles
(1.28.2) via an MSBuild target; `--smoke` proves sherpa recognizer + our graph run in one process.
All runs print `onnxruntime.dll next to exe: 1.30.0...`.

## 3. Results on four English files (transcripts under `csharp/SpeakerTranscript/runs/`)

| File | Duration | Diarization | ASR | Transcript shape |
|---|---|---|---|---|
| `1-two-speakers-en.wav` (k2-fsa) | 16.0 s | 4 segments (3.2 s) | whole-file, timestamps (4.8 s) | 4 clean utterances, speaker alternation correct (2+2) |
| `2-two-speakers-en.wav` (k2-fsa) | 34.0 s | 14 segments (13.6 s) | per-turn (13.4 s) | 14 lines; coherent Mark-I interview, correct alternation |
| `two_speaker_fixture.wav` (synthetic) | 51.5 s | 8 segments (30.1 s) | per-turn (15.6–17.4 s) | **8/8 ground-truth turns** recovered; repeated text = looped source, as designed |
| `diarization_example.wav` (NVIDIA 8-voice demo) | 97.6 s | 136 chunks | per-turn | coherent demo transcript; speakers 0–6 detected; overlap-heavy stretches partially garbled (expected for 8 simultaneous voices) |

### 3.1 The token-timestamp transcript (attention model, 16 s clip)

```
[Speaker 0] [00:00.000 - 00:03.579] A pencil with black lead writes best.
[Speaker 0] [00:03.659 - 00:06.619] The lamp shone with a steady green flame.
[Speaker 1] [00:07.199 - 00:11.600] Clothes and lodging are free to new men.
[Speaker 1] [00:12.179 - 00:14.760] The glow deepened in the eyes of the sweet girl.
```

Notes: this clip reads Harvard sentences; the two speakers split 2+2 as heard. Token timestamps have
quirks (the first token's timestamp is 0; whisper fragments like `' Cl'`+`'othes'` carry odd offsets) —
the utterance segmenter guards against fragments/punctuation/degenerate timestamps; a `' Cl'` lead-in
shifts one utterance start ~2 s early (cosmetic; speaker assignment unaffected).

## 4. What remains / not verified (explicit)

- **Human listening review is still owed** (the task's manual-review criterion). Objective cross-checks
  pass (fixture ground truth 8/8; turn structure vs a human read of clip metadata). The maintainer
  should listen to the four clips alongside the transcripts as the final acceptance step.
- Diarization/ASR timings above are CPU wall-clock on this machine — not a benchmark (no mobile, no
  int8-vs-fp32 comparisons; out of scope: Persian, UI, storage, confidence).
- The attention-enabled export workflow is validated but has upstream roughness; if it becomes core,
  pin the exporter/torch combination in a script and re-check against sherpa releases.
- Memory usage not instrumented.

## 5. Reproduce

See `spikes/PH0-SPIKE-002/README.md` § Reproduce. Attention-model export:
```powershell
cd spikes\PH0-SPIKE-002\artifacts\whisper-export
# export-onnx-with-attention.py + export_onnx.py fetched from k2-fsa/sherpa-onnx scripts/whisper,
# patched with the legacy-exporter shim (see 2.4), then:
python export-onnx-with-attention.py --model small
# run with the exported models:
SpeakerTranscript.exe --audio <wav> --whisper-dir ..\..\artifacts\whisper-export
```

## 6. ADR impact

None. ADR-0003's sherpa-for-ASR decision is confirmed viable here (with the two documented constraints);
nothing in this spike contradicts an accepted ADR, so no new ADR is proposed.
