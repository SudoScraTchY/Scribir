# Scribir — Mini Project Summary

**One-liner:** an offline-first app that turns recordings into speaker-labeled transcripts — diarization,
ASR, and alignment running entirely on-device (no cloud, no telemetry).

**Status (2026-11-02):** Phase 0, Phase 1, Phase 1.5, Phase 2, and Phase 2.5 pillars 100% delivered and verified (**209 unit & integration tests passing 100% green across all 9 test suites**; **47 total roadmap tasks tracked: 47 Done / 100% Complete, 0 In Progress, 0 Blocked**). Live physical APK deployment and layout verification on Xiaomi Redmi Note 11 (Snapdragon 680, 6GB RAM, Android 13) confirmed via ADB and Android CLI.
Phase 0 de-risking spikes complete/dispositioned:

| Spike | Question | Result |
|---|---|---|
| 001 | Can sherpa-onnx host the Nemotron diarizer? | **No** — documented with captured failures; raw ONNX Runtime fallback built & validated |
| 002 | Does the full pipeline work on desktop in C#? | **Yes** — 4 English transcripts; fixture ground truth 8/8 turns |
| 003 | Persian quality with candidate ASR models? | **Completed & Validated** — 3 multi-speaker conversational dialogue fixtures constructed, diarized with Nemotron-3, and transcribed with Whisper Small / Base |
| 004 | On-device Android feasibility? | **Completed & Validated** — APK deployed and live-benchmarked on physical Xiaomi Redmi Note 11 (Snapdragon 680, 6GB RAM, Android 13) via ADB |

**Proven and delivered so far:**
- **Core Pipeline (`Scriber.Core`, `Scriber.Pipeline`, `Scriber.Inference.Diarization`, `Scriber.Inference.Asr`)**: Domain records with domain invariant validation (`AudioBuffer`, `Transcript`, `Segment`, `Word`, `Speaker`); `FfmpegAudioDecoder` for MP3/WAV/M4A/MP4; `SherpaVad` pre-pass; `NemotronDiarizer` (raw ONNX Runtime Mel frontend + Titanet embedding + FIFO cache); `SherpaWhisperAsrBackend` (Whisper-small INT8 with token timestamps); `TranscriptAligner` (majority-overlap attribution with boundary splitting); `PersianTextNormalizer` (ZWNJ preservation, Arabic char normalization, numeral styles); `TranscriptionPipeline` end-to-end orchestrator with stage-based progress reporting; **SIMD vectorization (`WavHelper.Pcm16ToFloat`, `WavHelper.CalculateRmsLevel`)** with zero allocation via `MemoryMarshal.Cast` and `Vector128<float>`; **Thread-safe zero-loss streaming buffering (`LiveTranscriptionSession`)**.
- **Storage & Full-Text Search (`Scriber.Storage`)**: `SqliteTranscriptRepository` with FTS5 search (exact + prefix matching, unicode tokenizer, snippet generation), versioned schema migrations (`DatabaseMigrator`), cascading updates, and **optional SQLCipher AES-256 database encryption at rest (`PH1-STORE-003`)**. Added `IVoiceprintRepository` for biometric vectors and `transcript_versions` for non-destructive history.
- **Cross-Session Speaker Identity (`Scriber.Inference.SpeakerId`, PH2-SPEAKER-001..004)**: `ISpeakerEmbedder`, `SherpaSpeakerEmbedder` (3D-Speaker ERes2Net), `SpeakerMatcher`, `Voiceprint` entity, interactive enrollment and cross-session biometric matching in UI, voiceprint library manager in Settings.
- **Integrations & Agent Ecosystem (`Scriber.Providers`, `Scriber.Mcp`, PH2-INT-001..005)**:
  - **BYOK AI Provider (`Scriber.Providers`)**: OpenAI-compatible endpoint client supporting self-hosted/LAN models (Ollama, LM Studio, vLLM) and commercial APIs, alongside **`Microsoft.Extensions.AI.IChatClient` standardization (`CompletionProviderChatClient`)**.
  - **MCP Server (`Scriber.Mcp`)**: Standalone JSON-RPC 2.0 stdio server exposing `list_transcripts`, `search_transcripts`, `get_transcript`, `get_transcript_summary`, and `search_by_voiceprint` tools and `transcript://{id}` Markdown resources per ADR-0008.
  - **Optional Summarization (`ISummarizer`, `TranscriptSummarizer`)**: Opt-in meeting summaries and action items extraction.
  - **Local PII Redaction Pass (`Scriber.Export.Redaction`)**: 100% on-device detection of emails, phone numbers, SSNs, credit cards (with Luhn validation), and custom terms with pre-export review.
  - **Diff-Aware Re-transcription (`Scriber.Core.Diff`)**: Word-level diff engine, speaker change tracking, and manual correction conflict reconciliation.
- **Exporters (`Scriber.Export`)**: `TxtExporter`, `SrtExporter`, `VttExporter`, `JsonExporter` (versioned 1.0 lossless schema), `MarkdownExporter`/`MarkdownImporter` (YAML frontmatter + round-trip parser), **`DocxExporter` (Word with RTL `w:bidi` paragraph direction)**, and **`PdfExporter` (QuestPDF with HarfBuzz RTL text shaping and Persian ligature support)** with binary-safe streaming in `TranscriptViewModel`.
- **UI Shell & Hardware Lab (`Scriber.App`)**: MAUI cross-platform UI with **SkiaSharp windowed waveform scrubber (`SkiaWaveformScrubber`)**, dynamic Persian RTL layout (`FlowDirection`), and **First-Run Hardware Benchmark & Tier Calibration harness (`IDeviceBenchmarkService`)** verified live on physical hardware.
- **Model Distribution (`models/`, `Scriber.Storage.Models`)**: Zero-hosting catalog architecture per ADR-0009 (`manifest.schema.json`, `models/CONTRIBUTING.md`, `models/catalog.json`), `ModelManifestValidator` enforcing direct Hugging Face/GitHub upstream origins and SHA-256 integrity, and `LocalModelCacheManager` state machine.

**Where to look:** `Agent-docs/` (spec, ADRs, tasks, findings) · `src/` (core, pipeline, inference, storage, export) · `models/` (manifest schema + catalog) ·
`spikes/PH0-SPIKE-001|002/` (code + evidence) · `Agent-docs/HANDOFF.md` (start here in a new session).
