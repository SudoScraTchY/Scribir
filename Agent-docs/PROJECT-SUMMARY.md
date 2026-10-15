# Scribir — Mini Project Summary

**One-liner:** an offline-first app that turns recordings into speaker-labeled transcripts — diarization,
ASR, and alignment running entirely on-device (no cloud, no telemetry).

**Status (2026-10-15):** Phase 1 core pipeline, storage, export, and model manifest systems fully implemented and verified (74 unit & integration tests passing across 6 test suites).
Phase 0 de-risking spikes complete/dispositioned:

| Spike | Question | Result |
|---|---|---|
| 001 | Can sherpa-onnx host the Nemotron diarizer? | **No** — documented with captured failures; raw ONNX Runtime fallback built & validated |
| 002 | Does the full pipeline work on desktop in C#? | **Yes** — 4 English transcripts; fixture ground truth 8/8 turns |
| 003 | Persian quality with candidate ASR models? | **On Hold** — blocked on inputs (no personal test recordings); Phase 1 unblocked |
| 004 | On-device Android feasibility? | **Deferred** — deferred to a much later date; desktop first |

**Proven and delivered so far:**
- **Core Pipeline (`Scriber.Core`, `Scriber.Pipeline`, `Scriber.Inference.Diarization`, `Scriber.Inference.Asr`)**: Domain records with domain invariant validation (`AudioBuffer`, `Transcript`, `Segment`, `Word`, `Speaker`); `FfmpegAudioDecoder` for MP3/WAV/M4A/MP4; `SherpaVad` pre-pass; `NemotronDiarizer` (raw ONNX Runtime Mel frontend + Titanet embedding + FIFO cache); `SherpaWhisperAsrBackend` (Whisper-small INT8 with token timestamps); `TranscriptAligner` (majority-overlap attribution with boundary splitting); `PersianTextNormalizer` (ZWNJ preservation, Arabic char normalization, numeral styles); `TranscriptionPipeline` end-to-end orchestrator with stage-based progress reporting.
- **Storage & Full-Text Search (`Scriber.Storage`)**: `SqliteTranscriptRepository` with FTS5 search (exact + prefix matching, unicode tokenizer, snippet generation), versioned schema migrations (`DatabaseMigrator`), and cascading updates.
- **Exporters (`Scriber.Export`)**: `TxtExporter`, `SrtExporter`, `VttExporter`, `JsonExporter` (versioned 1.0 lossless schema), and `MarkdownExporter`/`MarkdownImporter` (YAML frontmatter + round-trip parser for PKM and Phase 2 MCP server per ADR-0008).
- **Model Distribution (`models/`, `Scriber.Storage.Models`)**: Zero-hosting catalog architecture per ADR-0009 (`manifest.schema.json`, `models/CONTRIBUTING.md`, `models/catalog.json`), `ModelManifestValidator` enforcing direct Hugging Face/GitHub upstream origins and SHA-256 integrity, and `LocalModelCacheManager` state machine.

**Where to look:** `Agent-docs/` (spec, ADRs, tasks, findings) · `src/` (core, pipeline, inference, storage, export) · `models/` (manifest schema + catalog) ·
`spikes/PH0-SPIKE-001|002/` (code + evidence) · `Agent-docs/HANDOFF.md` (start here in a new session).
