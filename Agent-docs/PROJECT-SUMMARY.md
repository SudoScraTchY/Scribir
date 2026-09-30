# Scribir — Mini Project Summary

**One-liner:** an offline-first app that turns recordings into speaker-labeled transcripts — diarization,
ASR, and alignment running entirely on-device (no cloud, no telemetry).

**Status (2026-09-30):** Phase 0 de-risking — 2 of 4 spikes done. No product code yet, by design: Phase 0
exists to retire the risky unknowns before any UI is built.

| Spike | Question | Result |
|---|---|---|
| 001 | Can sherpa-onnx host the Nemotron diarizer? | **No** — documented with captured failures; raw ONNX Runtime fallback built & validated |
| 002 | Does the full pipeline work on desktop in C#? | **Yes** — 4 English transcripts; fixture ground truth 8/8 turns |
| 003 | Persian quality with candidate ASR models? | Not started — blocked on real Persian recordings |
| 004 | On-device Android feasibility? | Not started — blocked on 2 Android devices |

**Proven so far:** a custom ONNX export of NVIDIA's Nemotron-3-Diarization (streaming Sortformer)
validated to ≤7.6e-5 per-step vs the torch reference; a native C# diarizer port that is segment-identical
to the reference; sherpa-onnx Whisper small int8 ASR working with both per-turn and token-timestamp
alignment; two ONNX Runtimes coexisting in one process.

**Where to look:** `Agent-docs/` (spec, ADRs, tasks, findings) · `spikes/PH0-SPIKE-001|002/` (code +
evidence) · `Agent-docs/HANDOFF.md` (start here in a new session).
