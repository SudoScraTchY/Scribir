# Scribir Task Index & Roadmap Status

This index tracks all 47 engineering tasks across Phases 0 through 2.5 of the Scribir project. It reconciles the Product Specification SDD ([`Agent-docs/product/01-product-specification.md`](../product/01-product-specification.md)), all Architecture Decision Records ([`Agent-docs/adr/`](../adr/)), and active implementation tickets.

## Status Overview

| Status | Count | Percentage |
| :--- | :---: | :---: |
| **Done / Complete** | 47 | 100.0% |
| **In Progress** | 0 | 0.0% |
| **Blocked** | 0 | 0.0% |
| **Not Started** | 0 | 0.0% |
| **Total Tasks** | **47** | **100.0%** |

---

## Complete Task Catalog

| Task ID | Task Title | Phase | Status | Depends On | Related ADRs | File Location |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **PH0-SPIKE-001** | Validate sherpa-onnx against custom Nemotron-3 Diarization export | Phase 0 | **Done** | None | ADR-0003, ADR-0004 | [`phase0-spike.md`](phase0-spike.md) |
| **PH0-SPIKE-002** | End-to-end desktop proof of concept, one file, English | Phase 0 | **Done** | PH0-SPIKE-001 | ADR-0003, ADR-0004, ADR-0005 | [`phase0-spike.md`](phase0-spike.md) |
| **PH0-SPIKE-003** | Persian audio validation pass (spike) | Phase 0 | **Done** | PH0-SPIKE-002 | ADR-0004, ADR-0005 | [`phase0-spike.md`](phase0-spike.md) |
| **PH0-SPIKE-004** | On-device mobile feasibility benchmark, Android (spike) | Phase 0 | **Done** | PH0-SPIKE-002 | ADR-0003, ADR-0006, ADR-0011 | [`phase0-spike.md`](phase0-spike.md) |
| **PH1-CORE-001** | Define Scriber.Core domain models | Phase 1 | **Done** | None | ADR-0001 | [`phase1-core-pipeline.md`](phase1-core-pipeline.md) |
| **PH1-CORE-002** | Audio import & decode to normalized PCM | Phase 1 | **Done** | PH1-CORE-001 | ADR-0001, ADR-0012 | [`phase1-core-pipeline.md`](phase1-core-pipeline.md) |
| **PH1-CORE-003** | VAD pre-pass integration | Phase 1 | **Done** | PH1-CORE-002 | ADR-0003 | [`phase1-core-pipeline.md`](phase1-core-pipeline.md) |
| **PH1-CORE-004** | Diarization integration | Phase 1 | **Done** | PH1-CORE-002 | ADR-0003, ADR-0004 | [`phase1-core-pipeline.md`](phase1-core-pipeline.md) |
| **PH1-CORE-005** | Define IAsrBackend and first concrete backend | Phase 1 | **Done** | PH1-CORE-002 | ADR-0003, ADR-0005 | [`phase1-core-pipeline.md`](phase1-core-pipeline.md) |
| **PH1-CORE-006** | Alignment / merge pass | Phase 1 | **Done** | PH1-CORE-004, PH1-CORE-005 | ADR-0003, ADR-0004 | [`phase1-core-pipeline.md`](phase1-core-pipeline.md) |
| **PH1-CORE-007** | Persian-aware text normalization pass | Phase 1 | **Done** | PH1-CORE-006 | ADR-0005 | [`phase1-core-pipeline.md`](phase1-core-pipeline.md) |
| **PH1-STORE-001** | SQLite schema & repository for Transcript/Speaker/Segment | Phase 1 | **Done** | PH1-CORE-001 | ADR-0001 | [`phase1-storage-export.md`](phase1-storage-export.md) |
| **PH1-STORE-002** | Local model cache manager | Phase 1 | **Done** | None | ADR-0009 | [`phase1-storage-export.md`](phase1-storage-export.md) |
| **PH1-STORE-003** | Optional at-rest encryption | Phase 1 | **Done** | PH1-STORE-001 | ADR-0007 | [`phase1-storage-export.md`](phase1-storage-export.md) |
| **PH1-EXPORT-001** | TXT, SRT, VTT, JSON exporters | Phase 1 | **Done** | PH1-CORE-001 | ADR-0001 | [`phase1-storage-export.md`](phase1-storage-export.md) |
| **PH1-EXPORT-002** | Structured Markdown exporter with YAML frontmatter | Phase 1 | **Done** | PH1-CORE-001 | ADR-0001 | [`phase1-storage-export.md`](phase1-storage-export.md) |
| **PH1-EXPORT-003** | DOCX and PDF exporters | Phase 1 | **Done** | PH1-CORE-001 | ADR-0001 | [`phase1-storage-export.md`](phase1-storage-export.md) |
| **PH1-UI-001** | App shell navigation | Phase 1 | **Done** | None | ADR-0001, ADR-0002 | [`phase1-ui-shell.md`](phase1-ui-shell.md) |
| **PH1-UI-002** | Import & transcribe flow screen | Phase 1 | **Done** | PH1-UI-001, PH1-CORE-006 | ADR-0001, ADR-0002 | [`phase1-ui-shell.md`](phase1-ui-shell.md) |
| **PH1-UI-003** | Transcript view | Phase 1 | **Done** | PH1-UI-001, PH1-CORE-001 | ADR-0001, ADR-0002 | [`phase1-ui-shell.md`](phase1-ui-shell.md) |
| **PH1-UI-004** | Waveform rendering via SkiaSharp | Phase 1 | **Done** | PH1-UI-003 | ADR-0002 | [`phase1-ui-shell.md`](phase1-ui-shell.md) |
| **PH1-UI-005** | Speaker renaming and retroactive relabeling | Phase 1 | **Done** | PH1-UI-003, PH1-STORE-001 | ADR-0001 | [`phase1-ui-shell.md`](phase1-ui-shell.md) |
| **PH1-UI-006** | RTL layout pass | Phase 1 | **Done** | PH1-UI-003 | ADR-0002 | [`phase1-ui-shell.md`](phase1-ui-shell.md) |
| **PH1-UI-007** | Confidence-aware review styling | Phase 1 | **Done** | PH1-UI-003 | ADR-0002 | [`phase1-ui-shell.md`](phase1-ui-shell.md) |
| **PH1-UI-008** | Library/search screen | Phase 1 | **Done** | PH1-UI-001, PH1-STORE-001 | ADR-0001 | [`phase1-ui-shell.md`](phase1-ui-shell.md) |
| **PH1-MODEL-001** | Model manifest schema | Phase 1 | **Done** | None | ADR-0009 | [`phase1-model-manager.md`](phase1-model-manager.md) |
| **PH1-MODEL-002** | Model Manager UI | Phase 1 | **Done** | PH1-MODEL-001, PH1-STORE-002 | ADR-0002, ADR-0009 | [`phase1-model-manager.md`](phase1-model-manager.md) |
| **PH1-MODEL-003** | First-run device benchmark harness | Phase 1 | **Done** | PH1-MODEL-001, PH1-CORE-005 | ADR-0006, ADR-0009 | [`phase1-model-manager.md`](phase1-model-manager.md) |
| **PH1-MODEL-004** | Populate initial manifest entries | Phase 1 | **Done** | PH1-MODEL-001 | ADR-0005, ADR-0009 | [`phase1-model-manager.md`](phase1-model-manager.md) |
| **PH1.5-REC-001** | Microphone capture | Phase 1.5 | **Done** | PH1-CORE-002 | ADR-0001, ADR-0012 | [`phase1.5-recording-streaming.md`](phase1.5-recording-streaming.md) |
| **PH1.5-REC-002** | Streaming diarization + streaming ASR wiring | Phase 1.5 | **Done** | PH1.5-REC-001, PH1-CORE-006 | ADR-0003, ADR-0004 | [`phase1.5-recording-streaming.md`](phase1.5-recording-streaming.md) |
| **PH1.5-REC-003** | Two-pass finalization | Phase 1.5 | **Done** | PH1.5-REC-002 | ADR-0003, ADR-0004 | [`phase1.5-recording-streaming.md`](phase1.5-recording-streaming.md) |
| **PH1.5-REC-004** | Pause/resume recording UI | Phase 1.5 | **Done** | PH1.5-REC-001, PH1-UI-003 | ADR-0001, ADR-0002 | [`phase1.5-recording-streaming.md`](phase1.5-recording-streaming.md) |
| **PH1.5-REC-005** | System-audio loopback capture investigation (spike) | Phase 1.5 | **Done** | PH1.5-REC-001 | ADR-0011, ADR-0012 | [`phase1.5-recording-streaming.md`](phase1.5-recording-streaming.md) |
| **PH2-SPEAKER-001**| Speaker embedding extraction | Phase 2 | **Done** | PH1-CORE-004 | ADR-0010 | [`phase2-speaker-identity.md`](phase2-speaker-identity.md) |
| **PH2-SPEAKER-002**| Enrollment flow | Phase 2 | **Done** | PH2-SPEAKER-001, PH1-STORE-001 | ADR-0010 | [`phase2-speaker-identity.md`](phase2-speaker-identity.md) |
| **PH2-SPEAKER-003**| Cross-session matching | Phase 2 | **Done** | PH2-SPEAKER-002, PH1-CORE-006 | ADR-0010 | [`phase2-speaker-identity.md`](phase2-speaker-identity.md) |
| **PH2-SPEAKER-004**| Voiceprint library management UI | Phase 2 | **Done** | PH2-SPEAKER-002, PH1-UI-001 | ADR-0002, ADR-0010 | [`phase2-speaker-identity.md`](phase2-speaker-identity.md) |
| **PH2-INT-001** | BYOK provider configuration | Phase 2 | **Done** | PH1-STORE-001 | ADR-0007 | [`phase2-integrations-mcp-provider-redaction.md`](phase2-integrations-mcp-provider-redaction.md) |
| **PH2-INT-002** | MCP server exposing transcripts and search | Phase 2 | **Done** | PH1-STORE-001 | ADR-0008 | [`phase2-integrations-mcp-provider-redaction.md`](phase2-integrations-mcp-provider-redaction.md) |
| **PH2-INT-003** | Optional local LLM summarization / action-item extraction | Phase 2 | **Done** | PH2-INT-001 | ADR-0007 | [`phase2-integrations-mcp-provider-redaction.md`](phase2-integrations-mcp-provider-redaction.md) |
| **PH2-INT-004** | Local PII / redaction pass | Phase 2 | **Done** | PH1-CORE-001 | ADR-0007 | [`phase2-integrations-mcp-provider-redaction.md`](phase2-integrations-mcp-provider-redaction.md) |
| **PH2-INT-005** | Diff-aware re-transcription | Phase 2 | **Done** | PH1-CORE-006, PH1-STORE-001 | ADR-0004, ADR-0005 | [`phase2-integrations-mcp-provider-redaction.md`](phase2-integrations-mcp-provider-redaction.md) |
| **PH2-ASR-002** | Custom vocabulary & word-boosting in ASR pipeline | Phase 2 | **Done** | PH1-CORE-005, PH1-CORE-006 | ADR-0005, ADR-0007 | [`phase2-integrations-mcp-provider-redaction.md`](phase2-integrations-mcp-provider-redaction.md) |
| **PH2-UI-009** | Multi-track diarization timeline view in SkiaSharp | Phase 2 | **Done** | PH1-UI-004, PH1-CORE-004 | ADR-0002, ADR-0004 | [`phase1-ui-shell.md`](phase1-ui-shell.md) |
| **PH2.5-PLAT-001** | Cross-platform share-sheet & media launch integration | Phase 2.5 | **Done** | PH1-UI-002 | ADR-0001, ADR-0011 | [`phase2.5-platform-depth.md`](phase2.5-platform-depth.md) |
| **PH2.5-SYNC-001** | Decentralized self-hosted transcript synchronization | Phase 2.5 | **Done** | PH1-STORE-001, PH1-STORE-003 | ADR-0007, ADR-0009 | [`phase2.5-platform-depth.md`](phase2.5-platform-depth.md) |
