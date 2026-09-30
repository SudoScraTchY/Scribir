# Scribir — Agent Handoff (2026-09-30)

Read this first when picking up Scribir in a new session. Companion doc: `PROJECT-SUMMARY.md` (shorter,
paste-ready). Both live in the repo; keep them updated as work lands.

## 1. What Scribir is

Offline-first, speaker-attributed transcription app (Windows first, Android later): local audio in →
speaker-labeled transcript out (diarization + ASR + alignment + redaction/export), fully on-device.
.NET MAUI / C# app over thin native inference layers. The human maintainer has a professional C#/.NET
background and wants claims backed by measurements.

## 2. Authoritative spec — read before coding

`Agent-docs/` is the engineering bundle and the single source of truth:

- `README.md` — bundle map + prescribed reading order (ADRs → product docs → tasks).
- `product/` — product specification, architecture, model strategy.
- `adr/` — 12 accepted ADRs (immutable; supersede with new ADRs, never edit).
- `tasks/phase0-spike.md` — the current work list; **update Status fields directly as you work**.
- `findings/` — `PH0-SPIKE-001.md`, `PH0-SPIKE-002.md` — read both before touching the diarizer or ASR.

## 3. Current state (2026-09-30)

- **PH0-SPIKE-001 — Done.** sherpa-onnx (v1.13.8) **cannot** host `nvidia/Nemotron-3-Diarization`
  (no streaming diarization API; pyannote-only segmentation slot; two native failure modes captured).
  A custom ONNX **step-graph export** was built and validated (≤7.6e-5 streaming / 1.5e-4 offline vs the
  torch reference). Adopted runtime path: **raw ONNX Runtime for the diarizer; sherpa stays for
  VAD/ASR/embedding** (ADR-0003's anticipated partial fallback).
- **PH0-SPIKE-002 — Done.** Full desktop pipeline proven in C#:
  `spikes/PH0-SPIKE-002/csharp/SpeakerTranscript` — native C# diarizer port (all reference gates pass;
  segment-identical to the Python reference) + sherpa Whisper small int8 (multilingual) + naive align.
  Four English transcripts captured; the synthetic fixture's 8 ground-truth turns recovered 8/8.
- **PH0-SPIKE-003 — Not Started, blocked on inputs:** needs 3–5 *real* Persian multi-speaker recordings
  (user/community-provided) plus the Persian ASR candidates from ADR-0005.
- **PH0-SPIKE-004 — Not Started, blocked on hardware:** two Android devices (modern + low-end).
- Commits: `13df101` (docs baseline) → `5b4d6fe` + `a8db688` (spike 001) → `c7e7e6a` (spike 002).
  Working tree clean.

## 4. Key technical facts (do not re-derive)

- Diarizer model: `nvidia/Nemotron-3-Diarization` (Hugging Face, ungated, OpenMDW-1.1; Streaming
  Sortformer, ~100M params, 8 speakers). **No official ONNX exists** — our export scripts are the
  reference: `spikes/PH0-SPIKE-001/python/01_export_step_onnx.py` (v1) and
  `07_export_step_onnx_masked.py` (v2, mask-aware — the recommended contract).
- Step-graph contract (v2): inputs `input_features [1,Tm,128]` + `cached_embeds [1,L,512]` +
  `enc_mask [1,L+E]`; output `chunk_logits [1,(L+E)*8,8]`. The AOSC/FIFO **speaker cache is host-side**
  (semantics in findings PH0-SPIKE-001 §4.2; port assets in `spikes/PH0-SPIKE-001/artifacts/csharp-port-assets/`).
- The **C# port already exists and is gate-validated**: `spikes/PH0-SPIKE-002/csharp/DiarizerPort`
  (`--check` runs all gates; report under `runs/check/`).
- sherpa-onnx 1.13.8 constraints: offline Whisper decodes **<30 s per call**; token timestamps require a
  **cross-attention-enabled decoder** — we exported one (findings PH0-SPIKE-002 §2.4; on torch 2.14 the
  dynamo export must be forced to the legacy exporter; shim documented in the findings note).
- Two ONNX Runtimes in one process: pin **Microsoft.ML.OnnxRuntime 1.30.0**'s `onnxruntime.dll` over
  sherpa's bundled 1.28.2 (MSBuild target in `SpeakerTranscript.csproj`; `--smoke` verifies coexistence).
- Python side lives in `spikes/PH0-SPIKE-001/python/.venv` (torch 2.14 CPU, transformers 5.18.0.dev0
  @ `f5af3202`, onnx 1.23, onnxruntime 1.30, librosa, openai-whisper) — the venv is uv-managed (no pip);
  get versions via `uv pip freeze --python .venv\Scripts\python.exe` (recorded in `python/logs/freeze.txt`).

## 5. Machine quirks (this workstation)

- `dotnet` on PATH resolves to AutoClaw's bundled runtime — use `C:\Program Files\dotnet\dotnet.exe`
  and set `DOTNET_ROOT=C:\Program Files\dotnet` (or .NET 9 apps won't start).
- Console codepage is cp1252 — set `PYTHONUTF8=1` for every Python run (torch's progress output crashes
  otherwise).
- Keep caches/venvs on M: (`UV_CACHE_DIR`, `UV_PYTHON_INSTALL_DIR`, `PIP_CACHE_DIR`, `HF_HOME` under
  `M:\Agentic\Projects\Scribir\.cache`); C: has limited free space.
- GPU is a GTX 1660 (Turing) — outside NVIDIA's supported accelerator list for this model; everything so
  far is validated **CPU-only by design**.

## 6. Fast verification (≈5 min)

```powershell
cd M:\Agentic\Projects\Scribir\spikes\PH0-SPIKE-002\csharp\DiarizerPort
$env:DOTNET_ROOT='C:\Program Files\dotnet'
'C:\Program Files\dotnet\dotnet.exe' build -c Release
.\bin\Release\net9.0\win-x64\DiarizerPort.exe --check     # expect: ALL CHECKS PASSED
```

## 7. Suggested next steps

1. PH0-SPIKE-003 as soon as Persian recordings exist (the console already accepts `--audio` and
   `--whisper-dir`, so the harness extension is small).
2. PH0-SPIKE-004 when two Android devices are available (build the instrumentation harness first).
3. Optional follow-ups from spike 001: evaluate community ONNX conversions for the model manifest
   (ADR-0009 angle); pin the attention-export toolchain if word-level timestamps become core.
4. When Phase 0 closes, start Phase 1 per `tasks/phase1-*.md` (build tasks) — read the phase file and
   its dependencies before writing product code.

## 8. Working conventions

- Evidence lives in the repo: findings notes in `Agent-docs/findings/`, raw logs under
  `spikes/*/csharp/runs/` and `spikes/*/python/logs/`; model weights / big binaries are git-ignored
  under `spikes/**/artifacts/` (regeneration is documented in each README/findings).
- Update task Status fields in `Agent-docs/tasks/*.md` as work progresses; add dated Completion notes.
- One commit per spike; message style `PH0-SPIKE-00X: <what> validated; task Done`.
- Never modify `Agent-docs/adr/*` (immutable) and never treat findings notes as spec — the bundle is.
