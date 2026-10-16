# Scribir — Mini Project Summary

**One-liner:** an offline-first app that turns recordings into speaker-labeled transcripts — diarization,
ASR, and alignment running entirely on-device (no cloud, no telemetry).

**Status (2026-10-16):** Phase 1 and Phase 2 core pillars fully implemented and verified (120 unit & integration tests passing across 9 test suites).
Phase 0 de-risking spikes complete/dispositioned:

| Spike | Question | Result |
|---|---|---|
| 001 | Can sherpa-onnx host the Nemotron diarizer? | **No** — documented with captured failures; raw ONNX Runtime fallback built & validated |
| 002 | Does the full pipeline work on desktop in C#? | **Yes** — 4 English transcripts; fixture ground truth 8/8 turns |
| 003 | Persian quality with candidate ASR models? | **On Hold** — blocked on inputs (no personal test recordings); Phase 1 unblocked |
| 004 | On-device Android feasibility? | **In Progress / Connected** — Xiaomi Redmi Note 11 (Snapdragon 680, 6GB RAM, Android 13) connected via ADB |

**Proven and delivered so far:**
- **Core Pipeline (`Scriber.Core`, `Scriber.Pipeline`, `Scriber.Inference.Diarization`, `Scriber.Inference.Asr`)**: Domain records with domain invariant validation (`AudioBuffer`, `Transcript`, `Segment`, `Word`, `Speaker`); `FfmpegAudioDecoder` for MP3/WAV/M4A/MP4; `SherpaVad` pre-pass; `NemotronDiarizer` (raw ONNX Runtime Mel frontend + Titanet embedding + FIFO cache); `SherpaWhisperAsrBackend` (Whisper-small INT8 with token timestamps); `TranscriptAligner` (majority-overlap attribution with boundary splitting); `PersianTextNormalizer` (ZWNJ preservation, Arabic char normalization, numeral styles); `TranscriptionPipeline` end-to-end orchestrator with stage-based progress reporting.
- **Storage & Full-Text Search (`Scriber.Storage`)**: `SqliteTranscriptRepository` with FTS5 search (exact + prefix matching, unicode tokenizer, snippet generation), versioned schema migrations (`DatabaseMigrator`), and cascading updates. Added `IVoiceprintRepository` for biometric vectors and `transcript_versions` for non-destructive history.
- **Cross-Session Speaker Identity (`Scriber.Inference.SpeakerId`, PH2-SPEAKER-001..004)**: `ISpeakerEmbedder`, `SherpaSpeakerEmbedder` (3D-Speaker ERes2Net), `SpeakerMatcher`, `Voiceprint` entity, interactive enrollment and cross-session biometric matching in UI, voiceprint library manager in Settings.
- **Integrations & Agent Ecosystem (`Scriber.Providers`, `Scriber.Mcp`, PH2-INT-001..005)**:
  - **BYOK AI Provider (`Scriber.Providers`)**: OpenAI-compatible endpoint client supporting self-hosted/LAN models (Ollama, LM Studio, vLLM) and commercial APIs with zero general internet requirements.
  - **MCP Server (`Scriber.Mcp`)**: Standalone JSON-RPC 2.0 stdio server exposing `list_transcripts`, `search_transcripts`, `get_transcript` tools and `transcript://{id}` Markdown resources per ADR-0008.
  - **Optional Summarization (`ISummarizer`, `TranscriptSummarizer`)**: Opt-in meeting summaries and action items extraction.
  - **Local PII Redaction Pass (`Scriber.Export.Redaction`)**: 100% on-device detection of emails, phone numbers, SSNs, credit cards (with Luhn validation), and custom terms with pre-export review.
  - **Diff-Aware Re-transcription (`Scriber.Core.Diff`)**: Word-level diff engine, speaker change tracking, and manual correction conflict reconciliation.
- **Exporters (`Scriber.Export`)**: `TxtExporter`, `SrtExporter`, `VttExporter`, `JsonExporter` (versioned 1.0 lossless schema), and `MarkdownExporter`/`MarkdownImporter` (YAML frontmatter + round-trip parser for PKM and Phase 2 MCP server per ADR-0008).
- **Model Distribution (`models/`, `Scriber.Storage.Models`)**: Zero-hosting catalog architecture per ADR-0009 (`manifest.schema.json`, `models/CONTRIBUTING.md`, `models/catalog.json`), `ModelManifestValidator` enforcing direct Hugging Face/GitHub upstream origins and SHA-256 integrity, and `LocalModelCacheManager` state machine.

**Where to look:** `Agent-docs/` (spec, ADRs, tasks, findings) · `src/` (core, pipeline, inference, storage, export) · `models/` (manifest schema + catalog) ·
`spikes/PH0-SPIKE-001|002/` (code + evidence) · `Agent-docs/HANDOFF.md` (start here in a new session).
