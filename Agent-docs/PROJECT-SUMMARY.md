# Scribir — Mini Project Summary

**One-liner:** an offline-first app that turns recordings into speaker-labeled transcripts — diarization,
ASR, and alignment running entirely on-device (no cloud, no telemetry).

**Status (2026-10-15):** Phase 1 Core Pipeline underway (CORE-001, 002, 003 Done; 25 unit tests passing).
Phase 0 de-risking spikes complete/dispositioned:

| Spike | Question | Result |
|---|---|---|
| 001 | Can sherpa-onnx host the Nemotron diarizer? | **No** — documented with captured failures; raw ONNX Runtime fallback built & validated |
| 002 | Does the full pipeline work on desktop in C#? | **Yes** — 4 English transcripts; fixture ground truth 8/8 turns |
| 003 | Persian quality with candidate ASR models? | **On Hold** — blocked on inputs (no personal test recordings); Phase 1 unblocked |
| 004 | On-device Android feasibility? | **Deferred** — deferred to a much later date; desktop first |

**Proven so far:** custom ONNX export of Nemotron-3-Diarization; native C# diarizer port; sherpa-onnx
Whisper small int8 ASR; dual ONNX Runtimes coexisting; Scriber.Core domain models with invariant validation;
ffmpeg audio decoding across MP3/WAV/M4A/MP4; sherpa-onnx Silero VAD pre-pass recovering 8/8 speech turns.
Model distribution is manifest-driven (GitHub JSON benchmark catalog) with direct Hugging Face client downloads (ADR-0009).

**Where to look:** `Agent-docs/` (spec, ADRs, tasks, findings) · `src/` (core pipeline) ·
`spikes/PH0-SPIKE-001|002/` (code + evidence) · `Agent-docs/HANDOFF.md` (start here in a new session).
