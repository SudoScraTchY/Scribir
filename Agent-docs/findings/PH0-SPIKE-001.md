# PH0-SPIKE-001 — Findings: sherpa-onnx vs. a custom Nemotron 3 Diarization ONNX export

**Status:** Done (spike) — deliverable is this findings note plus the scratch code it was validated with.
**Date:** 2026-09-29 (Asia/Tehran) · **Agent:** AutoCoder · **Task:** `PH0-SPIKE-001` in `tasks/phase0-spike.md`
**Machine:** Windows 10 x64 (DESKTOP-7K3PATG), CPU-only runs (GTX 1660 present; NVIDIA lists this model's
supported accelerators as Ampere+, so all validation intentionally used CPU paths).
**Authoritative source studied:** `nvidia/Nemotron-3-Diarization` (Hugging Face; OpenMDW-1.1; released 2026-09-23).

---

## TL;DR — the three questions the task asks

1. **Is sherpa-onnx's existing C# diarization API surface sufficient as-is to host a custom
   Nemotron 3 Diarization ONNX export? — NO.** Not for offline, and not for streaming. The API is a
   pyannote-style pipeline (segmentation model + speaker-embedding model + clustering, batch `Process()`
   only); it cannot express this model's architecture or its streaming state, and it fails *natively* on
   nonconforming models (see §3.4 for both failure modes, captured verbatim).
2. **Exactly what is missing:** (a) any streaming/online diarization API in any binding; (b) a
   single-model "end-to-end diarizer" slot — the only segmentation slot accepts a pyannote-contract
   ONNX (mandatory metadata keys + fixed I/O: waveform window in, powerset frames out); (c) session
   state I/O for the AOSC speaker cache + FIFO (no place to hand a step graph its cache tensors); (d)
   support for dynamic-step graphs (inputs sized per chunk, position ids restart per step); (e) known
   upstream: Sortformer support is an **open** feature request (k2-fsa/sherpa-onnx#3497), with community
   forks not merged.
3. **Recommendation: fall back to raw `Microsoft.ML.OnnxRuntime` for the diarizer** — exactly the
   partial-fallback outcome ADR-0003 anticipated as acceptable — while keeping sherpa-onnx for
   VAD/ASR/speaker-embedding. A working substitute path was built and validated end-to-end in this
   spike (export → ONNX Runtime → segments), including a C# run via `Microsoft.ML.OnnxRuntime`.

---

## 1. What was built (and where it lives)

All paths relative to the repo root (`M:\Agentic\Projects\Scribir`).

| Artifact | Path | Notes |
|---|---|---|
| Step-graph export script (no-mask variant, "v1") | `spikes/PH0-SPIKE-001/python/01_export_step_onnx.py` | torch.export → ONNX opset 18, dynamic `L`/`Tm` |
| Mask-aware export script ("v2") | `spikes/PH0-SPIKE-001/python/07_export_step_onnx_masked.py` | adds `enc_mask` input |
| Streaming validation (PyTorch vs ONNX, per step) | `spikes/PH0-SPIKE-001/python/02_run_streaming_ort.py` | runs on both test audios |
| Offline validation | `spikes/PH0-SPIKE-001/python/03_run_offline_ort.py` | external chunk drive, offline geometry |
| Synthetic 2-speaker fixture builder | `spikes/PH0-SPIKE-001/python/04_make_fixture.py` | ground truth by construction |
| C# fixtures export (per-step tensors) | `spikes/PH0-SPIKE-001/python/05_export_csharp_fixtures.py` | |
| Port assets dump (for future C# port) | `spikes/PH0-SPIKE-001/python/08_dump_port_assets.py` | filters, window, weights, reference vectors |
| Exported graphs | `spikes/PH0-SPIKE-001/artifacts/onnx/nemotron3_stream_step.onnx` (4.45 MB) and `nemotron3_stream_step_mask.onnx` (4.51 MB) | artifacts/ is git-ignored; scripts regenerate them |
| sherpa-onnx probe (C#) | `spikes/PH0-SPIKE-001/csharp/SherpaAttempt/` | reflection + control + experiment |
| Raw-ORT replay runner (C#) | `spikes/PH0-SPIKE-001/csharp/OrtRunner/` | Microsoft.ML.OnnxRuntime 1.30.0 |
| Raw logs / JSON evidence | `spikes/PH0-SPIKE-001/python/logs/`, `spikes/PH0-SPIKE-001/csharp/logs/` | all numbers below come from these |
| Test audio | `artifacts/models/diarization_example.mp3` (97.6 s, from HF `hf-internal-testing/dummy-audio-samples`), `artifacts/audio/two_speaker_fixture.wav` (51.5 s synthetic, exact ground truth) | |

### The exported "step graph" contract

The model is a chunked, stateful streaming diarizer; the graph represents **one processing step**:

- **v1 inputs:** `input_features` `[1, Tm, 128]` float32 (one chunk's log-mel, look-ahead frames included);
  `cached_embeds` `[1, L, 512]` float32 (speaker-cache + FIFO frames at *encoder-input* embeddings).
  **Output:** `chunk_logits` `[1, (L + ceil(Tm/8)) * 8, 8]` (per-frame × 8-speaker logits; host slices
  `[L*8 : (L+k)*8]` exactly as the reference does).
- **v2** adds `enc_mask` `[1, L + ceil(Tm/8)]` int64 → exact offline parity for masked tail frames.
- All axial dims are dynamic (single graph serves every latency preset and the offline geometry).
- The **cache update (AOSC pop/compress) stays host-side** — the same split used by the independent
  prior art (`altunenes/parakeet-rs` export script), and the only split sherpa-style APIs could
  *ever* express for this model.

Environment (recorded in `python/logs/freeze.txt`, `csharp/logs/dotnet_info.txt`):
Python 3.12.14 · torch 2.14.0+cpu · transformers **5.18.0.dev0** (git `f5af3202d63d9bb7578a41f0041c9040071e7345`,
the version the checkpoint was produced with) · onnx 1.23.0 · onnxruntime 1.30.0 · librosa · .NET SDK 9.0.300/10.0.301.

---

## 2. Method (why this reference is valid)

- The HF repo ships `.nemo` (NeMo), `model.safetensors` (Transformers-native), and a q8_0 GGUF
  (NeMo-Speech.cpp) — **there is no official ONNX export, and neither the model card nor
  `ASR_INTEGRATION_GUIDE.md` documents an ONNX path** (verified against the repo file tree and both
  documents; discussions #1–#4 contain no export info). The task's "follow its own reference pipeline"
  therefore resolved to: **reproduce the model's documented, NVIDIA-supported Transformers pipeline
  faithfully** (the model card's own offline + streaming usage sections; the checkpoint is built for
  `transformers` 5.18.0.dev0).
- The NeMo toolchain path was not used (Linux-oriented; heavy dependency chain on Windows). This is a
  recorded evidence gap, not a claim: parity is validated **within the Transformers reference**, and
  cross-checked against the model's own arithmetic (chunk geometry, cache sizes from `config.json`).
- Validation is **numerical, per step**: identical inputs through the PyTorch reference and the ONNX
  graph; logits max-abs-diff, cache-state (embeds/probs/fifo) max-abs-diff at every step, plus
  segment-level comparison against the reference and (for the synthetic fixture) against exact
  ground truth.

---

## 3. Results

### 3.1 Export & graph-level numerical validation (v1)
Max |Δ| vs the PyTorch wrapper for five size configurations (L = 0/5/37/104; Tm = 104; plus a short
last-chunk input):

| check | L | Tm | torch shape | onnx shape | max abs diff |
|---|---|---|---|---|---|
| first chunk | 0 | 104 | (1, 104, 8) | (1, 104, 8) | 1.53e-05 |
| small cache | 5 | 104 | (1, 144, 8) | (1, 144, 8) | 2.48e-05 |
| mid | 37 | 104 | (1, 400, 8) | (1, 400, 8) | 3.24e-05 |
| large | 104 | 104 | (1, 936, 8) | (1, 936, 8) | 3.82e-05 |
| short tail | 21 | 101 | (1, 272, 8) | (1, 272, 8) | 2.67e-05 |

(evidence: `python/logs/export_report.json`; v2 spot checks 1.5–3.4e-05 in `python/logs/v2_mask_validation.json`)

### 3.2 Streaming, end-to-end, on real audio

**A. `diarization_example.mp3` (97.6 s, 136 chunks, low-latency mode):**
- max per-step diff vs the PyTorch streaming reference: **logits 4.58e-05**, cache probs 1.09e-06,
  cache embeds/FIFO 0 (includes steps where the AOSC compression ran, `num_cache_frames` plateauing at 264).
- segments: torch 37 vs ONNX 37 — **identical within 55 ms tolerance** (torch offline mode: 29 segments;
  a different operating point, as expected).
- evidence: `python/logs/streaming_validation_example.json`, log `02_stream_example.log`.

**B. Synthetic fixture (51.5 s, 8 alternating turns, exact ground truth, 72 chunks):**
- all 8 ground-truth turns recovered with the correct alternating speakers; every segment identical
  between torch/ONNX; max logits diff 7.63e-05.
- detected vs truth (start–end, speaker): 0.30–5.87 s0 / 6.59–11.55 s1 / 13.31–18.87 s0 / 19.58–24.54 s1 /
  26.31–31.87 s0 / 32.58–37.55 s1 / 39.31–44.87 s0 / 45.58–50.54 s1 ↔ turns A 0–6, B 6.5–12.5, A 13–19,
  B 19.5–25.5, A 26–32, B 32.5–38.5, A 39–45, B 45.5–51.5 (systematic ≈0.3–0.6 s onset latency; expected
  boundary behavior, not a defect).
- evidence: `python/logs/streaming_validation_fixture.json`, `04_fixture.log`.

### 3.3 Offline configuration through the same step graph

- Offline geometry driven externally (chunk 340 enc frames / right 40 / FIFO 40 / update 300; 4 chunks
  for the example audio) reproduces the reference: **max abs diff 1.53e-04** with v2 (mask-aware),
  segments 29 vs 29 identical.
- Important nuance documented: the offline processor emits **one masked tail padding frame**
  (`attention_mask` zeros at index 9760 of 9761). Ignoring that mask (v1) still matches the *maskless*
  reference to 1.56e-04, but relative to the true masked reference the tail/adjacent frames differ by up
  to ~10 logits — v2's `enc_mask` input closes this exactly (10.06 → 1.53e-04). **v2 is the recommended
  integration contract.**
- evidence: `python/logs/offline_validation_example.json`, `python/logs/v2_mask_validation.json`.

### 3.4 sherpa-onnx probe (the core question), C# v1.13.8

Control (environment sanity) — sherpa's native stack on the synthetic fixture:
`pyannote-segmentation-3-0` + `3dspeaker eres2net` embedding ⇒ 10 segments, correct alternating
speakers, matching the fixture's turn structure. So the environment ran sherpa correctly.

API inventory (reflection over the C# assembly, quoted from `csharp/logs/sherpa_control.log`):

```
SherpaOnnx.OfflineSpeakerDiarization
SherpaOnnx.OfflineSpeakerDiarizationConfig
SherpaOnnx.OfflineSpeakerDiarizationProgressCallback
SherpaOnnx.OfflineSpeakerDiarizationSegment
—— any streaming/online diarization types: none
—— members: .ctor(config), Process(float[]), ProcessWithCallback(...), SampleRate, SetConfig
```

Structural facts (from the binding's config types): the only segmentation slot is
`Segmentation.Pyannote.Model` (+`WindowShiftRatio`); embedding via `Embedding.Model`; clustering via
`FastClusteringConfig`; min on/off durations. No cache/FIFO state, no per-chunk entry point, no
arbitrary end-to-end model slot.

**Experiment 1 — point sherpa at our exported graph as the "pyannote" segmentation model:**
native abort, exit −1:

```
offline-speaker-segmentation-pyannote-model.cc:Init:93 'sample_rate' does not exist in the metadata
```

**Experiment 2 — copy pyannote's metadata onto our model (to bypass validation) and retry:**
construction succeeds, then the native side throws while actually trying to use it:

```
EXCEPTION: System.Runtime.InteropServices.SEHException: External component has thrown an exception.
```

i.e. even camouflaged, the pipeline cannot feed/consume this model — the incompatibility is the I/O
contract and the missing streaming-state model, not just metadata. (The camouflaged copy is an
**experiment artifact** only: `artifacts/onnx/nemotron3_stream_step_meta.onnx`, not for any other use.)

Upstream status: **open** feature request "Sortformer support" (k2-fsa/sherpa-onnx **#3497**, filed
2026-04-10; a community fork with ~99.5% parity exists but is unmerged; last activity 2026-04-21).
No streaming diarization API exists in sherpa-onnx at this release.

### 3.5 Raw ONNX Runtime fallback, demonstrated in C# (`Microsoft.ML.OnnxRuntime` 1.30.0)

- 72 steps (the full fixture stream, including compressed-cache steps) run from C# against the exported
  graph with **dynamic input shapes** (`input_features[1,-1,128]`, `cached_embeds[1,-1,512]`).
- Bit-stable vs the Python-ORT expectation: **overall max abs diff 0.0** (same runtime);
  segments recomputed in C#: 8, matching Python's 8 exactly (`segments_match: true`).
- 31.4 s wall time for the 51.5 s clip, CPU, batch=1 (spike-grade; not a benchmark).
- evidence: `csharp/logs/csharp_results.json`, `csharp/logs/ort_replay.log`.

---

## 4. Recommendation (what the next tasks should assume)

1. **Diarizer runtime:** raw `Microsoft.ML.OnnxRuntime` (C#) + the **v2 step graph** + host-side AOSC
   state, per ADR-0003's anticipated partial fallback. Keep sherpa-onnx for VAD / ASR / speaker
   embedding (unchanged from ADR-0003's primary decision).
2. **Port spec for the host-side state** (the piece a C# port still owns):
   - state = `embeds [1,264,512]`, `probs [1,264,8]`, `fifo [1,F,512]` + 2 counters + 2 flags
     (F = 264 in streaming modes, 40 offline); reference semantics in `Nemotron3DiarizationSpeakerCache`
     (transformers) — concat-to-read, FIFO push, pop `max(update_period, overflow)` oldest frames into
     the cache, and score-based compression to 264 frames with 1 silence slot per speaker when the
     cache overflows (top-k on a log-odds score with a `latest_frames_score_boost` + two rank boosts;
     silence frames filled from the model's `silence_embeds`).
   - mel frontend port assets + reference vectors for self-validation: `artifacts/csharp-port-assets/`.
3. **Model distribution question for the app (ties to ADR-0009):** the original source has no ONNX
   files; the no-first-party-hosting rule means either (a) manifest points at community ONNX conversions
   (several now exist: `onnx-community/Nemotron-3-Diarization-ONNX` and others — untested by this
   spike), or (b) export-at-release into a downloadable artifact — which would need a follow-up ADR
   because hosting is constrained. Suggested text in Appendix A.
4. **If sherpa-onnx parity is ever desired**, the lever is upstream contribution (issue #3497), not
   local glue. The spike's evidence (two native failure modes + API inventory) is directly reusable for
   a `wontfix-for-now`/roadmap note there.

## 5. Not verified / evidence gaps (explicit)

- **NeMo runtime parity** not tested (Linux-oriented toolchain; transformers is an NVIDIA-documented
  runtime for this checkpoint). If strict NeMo-output parity is ever needed, export from `.nemo` per
  `parakeet-rs`'s script and diff against this graph.
- **Not tested in this spike:** onnx-community/other third-party ONNX conversions (different I/O
  contracts; one may be usable for C# directly — worth a follow-up), Android/mobile performance
  (`PH0-SPIKE-004`), Persian quality (`PH0-SPIKE-003`), quantization (INT8) — out of this task's scope.
- CPU-only numbers; no GPU path (the model card's supported list excludes this machine's GPU anyway).
- The C# demonstration drives the graph with per-step fixtures whose cache state is computed by the
  validated Python reference; the full C# port (mel + cache update in C#) is specified in §4.2 and
  backed by port assets, but **not implemented here** (that is product code, explicitly out of spike scope).
- sherpa-onnx probe run against v1.13.8 (current at 2026-09-10; master inspected 2026-09-29).

## 6. Reproduce (exact commands used; see also `spikes/PH0-SPIKE-001/README.md`)

```powershell
# one-time environment (already provisioned in this repo; caches and venv are git-ignored)
$env:UV_CACHE_DIR='M:\Agentic\Projects\Scribir\.cache\uv'; $env:UV_PYTHON_INSTALL_DIR='M:\Agentic\Projects\Scribir\.cache\python'
cd M:\Agentic\Projects\Scribir\spikes\PH0-SPIKE-001\python
uv venv --python 3.12 .venv
uv pip install --python .venv\Scripts\python.exe torch --index-url https://download.pytorch.org/whl/cpu
uv pip install --python .venv\Scripts\python.exe "transformers @ git+https://github.com/huggingface/transformers" onnx onnxruntime onnxscript soundfile librosa numpy

# pipeline (UTF-8 env matters on this console; PW: global dotnet must be invoked by full path)
$env:PYTHONUTF8='1'; $env:HF_HUB_OFFLINE='1'
..\python\.venv\Scripts\python.exe 01_export_step_onnx.py          # export v1 + checks
..\python\.venv\Scripts\python.exe 04_make_fixture.py              # synthetic fixture
..\python\.venv\Scripts\python.exe 02_run_streaming_ort.py --tag example
..\python\.venv\Scripts\python.exe 02_run_streaming_ort.py --audio ..\artifacts\audio\two_speaker_fixture.wav --tag fixture
..\python\.venv\Scripts\python.exe 07_export_step_onnx_masked.py   # v2 + offline validation
..\python\.venv\Scripts\python.exe 05_export_csharp_fixtures.py --tag fixture
..\python\.venv\Scripts\python.exe 08_dump_port_assets.py

# C#
$env:DOTNET_ROOT='C:\Program Files\dotnet'   # machine quirk: default DOTNET_ROOT points at a runtime without .NET 9
dotnet build -c Release  # in csharp\SherpaAttempt and csharp\OrtRunner (full CLI path: 'C:\Program Files\dotnet\dotnet.exe')
csharp\SherpaAttempt\bin\Release\net9.0\win-x64\SherpaAttempt.exe control  <fixture.wav> <sherpa-models dir> <onnx>
csharp\SherpaAttempt\bin\Release\net9.0\win-x64\SherpaAttempt.exe exp      <fixture.wav> <sherpa-models dir> <onnx>
csharp\OrtRunner\bin\Release\net9.0\win-x64\OrtRunner.exe <csharp-fixtures\fixture> <onnx> <out.json>
```

Environment quirks worth keeping in the repo's docs: this console is cp1252 → set `PYTHONUTF8=1` for
Python; the machine's `DOTNET_ROOT`/`PATH` resolve `dotnet` to a bundled runtime without .NET 9 →
use `C:\Program Files\dotnet\dotnet.exe` and set `DOTNET_ROOT` accordingly; keep all caches/venvs on `M:`
(C: has ~29 GB free).

## 7. ADR impact

ADR-0003's open sub-question ("can sherpa-onnx accommodate a custom export?") is now answered: **no —
use the anticipated partial fallback (raw ONNX Runtime for the diarizer)**. No decision is *changed*,
so no supersession is required by the task conventions. If the maintainer wants the closure recorded as
an ADR (suggested), Appendix A has ready text.

---

### Appendix A — suggested ADR text (for the maintainer; not yet a file)

> **ADR-0013 (proposed): Diarizer runtime — raw ONNX Runtime for Nemotron 3 Diarization**
> Status: Proposed · Supersedes: none (closes ADR-0003's open sub-question)
> Context: PH0-SPIKE-001 (findings: `Agent-docs/findings/PH0-SPIKE-001.md`) established that
> sherpa-onnx v1.13.8 cannot host this model: no streaming diarization API; the only segmentation slot
> requires a pyannote-contract ONNX (mandatory metadata; waveform→powerset I/O) and aborts natively on
> nonconforming models; upstream Sortformer support remains an open request (#3497).
> Decision: the diarizer runs through `Microsoft.ML.OnnxRuntime` in C# against a single-step ONNX graph
> (mask-aware variant preferred), with the AOSC/FIFO speaker cache maintained host-side; sherpa-onnx
> remains the integration layer for VAD, ASR, and speaker embedding per ADR-0003.
> Consequences: one custom runtime path for the diarizer; port assets and reference vectors are
> committed for the C# port; periodic re-evaluation if sherpa-onnx merges Sortformer support upstream.

### Appendix B — follow-up candidates (new tasks, not started)

1. `PH0-SPIKE-001-FU1`: C# port of mel frontend + AOSC state (uses `artifacts/csharp-port-assets/`), then
   re-run the fixture end-to-end in pure C#; optionally diff against `onnx-community` conversion.
2. `PH0-SPIKE-001-FU2`: evaluate third-party ONNX conversions for the model manifest (ADR-0009 angle).
3. `PH0-SPIKE-002`: end-to-end console proof (diarize + ASR + align) — note ASR selection still open;
   Nemotron-3.5-ASR / multitalker parakeet candidates found in the model's ASR guide.
