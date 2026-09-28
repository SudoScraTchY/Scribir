# Product Specification — Scriber (working name)

Status: Living document. Supersedes the v0.1 planning draft. Consolidates
all product decisions made through the current planning conversation.

## 1. One-line pitch

Scriber turns any recorded or live conversation into an accurate,
speaker-labeled transcript — entirely on your device, in your language,
for free, forever.

## 2. Product identity (non-negotiable constraints)

- **Free, open-source, never monetized.** No ads, no subscription, no
  paid tier, ever.
- **No first-party model hosting, no API reselling.** The app never
  bundles or proxies model weights through project infrastructure. The
  Model Manager links directly to original sources (principally Hugging
  Face) for every model. See ADR-0009.
- **Offline-first, not offline-only.** Full core functionality (transcribe
  a file or a live recording, with speaker labels) must work with zero
  network access once models are downloaded. Anything that touches the
  network beyond model download is additive and opt-in. See ADR-0007.
- **Two inference provider modes, no more:** Local on-device, and BYOK
  (bring-your-own-key, any OpenAI-compatible endpoint including a
  self-hosted one on the user's own LAN). See ADR-0007.

## 3. Target users, in priority order

1. Individuals recording their own meetings/interviews/lectures who want
   a private, free alternative to commercial cloud notetakers.
2. Non-English speakers underserved by commercial tools — Persian first,
   architecturally extensible to any language via the pluggable ASR
   backend (ADR-0005) and community model manifest (ADR-0009).
3. Privacy-constrained professionals (legal, medical, HR, journalism)
   who cannot legally or ethically pipe audio to a third-party cloud.
4. Developers/contributors who want a hackable, self-hostable
   transcription base, including via MCP (ADR-0008).
5. Low-connectivity or bandwidth-constrained users, for whom offline is
   a hard requirement, not a preference.

## 4. Platform scope (v1)

**Android and Desktop (Windows/macOS) are both required for v1.** iOS is
explicitly deferred. See ADR-0011 for the full reasoning (App Store
friction, no general system-audio loopback for third-party iOS apps,
engineering cost vs. marginal reach for this audience).

## 5. Non-goals

- Not a real-time cloud meeting bot that auto-joins Zoom/Meet/Teams (v2+
  at the earliest, and mostly a UI/automation problem, not a model one).
- Not an LLM-summarization product — summarization is optional and never
  required for the core value (an accurate, speaker-labeled transcript).
- Not chasing every last accuracy point against cloud SOTA. "Very good,
  free, and private" beats "state-of-the-art but requires internet."
- No first-party account system or server-side storage, ever (a user may
  point the app at their *own* self-hosted sync, e.g. WebDAV/Nextcloud/
  Syncthing, but the project runs no server of its own).

## 6. Core user flows

### Flow A — Import & transcribe
Import a file → waveform + language picker (auto-detect default) +
speaker-count hint (auto or manual) → on-device pipeline (VAD → diarize →
ASR → align → normalize) with per-stage progress → speaker-colored,
timestamp-synced, editable transcript, click-to-seek both directions →
export (TXT, SRT, VTT, structured Markdown, DOCX, PDF, JSON).

### Flow B — Live/offline recording
Record (mic; system-audio loopback is a separate, harder investigation —
see `tasks/phase1.5-recording-streaming.md`) → live partial transcript via
the models' streaming operating point → on stop, a second, higher-accuracy
offline pass reconciles with the live draft (two-pass design, matching
the diarizer and ASR models' own "same checkpoint, different latency
setting" architecture).

### Flow C — Speaker naming & correction
Generic `Speaker 1..N` labels ordered by first appearance → user renames,
applied retroactively → inline transcript editing for misrecognitions →
(v2) voice-profile enrollment for cross-session recognition, see ADR-0010.

### Flow D — Library, search, organize
Local, full-text-searchable library (SQLite/FTS) → folder/project
grouping → optional local encryption at rest.

### Flow E — Model & language management
A downloadable-model manifest browsed by language and device tier, each
entry showing size, license, expected accuracy, and a direct link to its
original source (never a project-hosted mirror) — see ADR-0009. A
first-run device benchmark measures real performance on the user's actual
hardware rather than asking them to self-report "is my device modern or
low-end" — see `tasks/phase1-model-manager.md`.

## 7. Feature list by phase

### Phase 0 — Spike (no product code)
De-risk the diarizer/ASR/runtime pairing before committing UI/product
engineering time. See `tasks/phase0-spike.md`.

### Phase 1 — MVP (Android + Desktop, simultaneously)
- Import + transcribe (Flow A), full pipeline
- Speaker-colored, editable, timestamp-synced transcript
- Confidence-aware review styling (visual flag on low-confidence
  words/segments — nobody in this space surfaces this today)
- Export: TXT, SRT, VTT, structured Markdown (with YAML frontmatter),
  DOCX, PDF, JSON
- Local library with full-text search
- Model Manager: browse manifest, download from source, license/size
  shown before download
- First-run device benchmark recommending a default model tier
- RTL layout, Persian font bundling, ZWNJ-correct text normalization
- Dark/light theme
- Fully offline after model download, verified by explicit airplane-mode
  testing

### Phase 1.5 — Recording & live
- In-app microphone recording, pause/resume
- Live streaming partial transcript + two-pass offline finalization
- System-audio loopback: a scoped, platform-by-platform investigation
  (Windows WASAPI, macOS ScreenCaptureKit/virtual device) — not assumed
  to ship simultaneously with the rest of Phase 1.5

### Phase 2 — Depth
- Cross-session speaker voice-profile enrollment and matching (ADR-0010)
- Diff-aware re-transcription (compare two passes of the same audio,
  e.g. after a model upgrade, without losing manual corrections)
- BYOK provider configuration (ADR-0007)
- MCP server exposing transcripts/search/summarize as tools+resources
  (ADR-0008) — the integration surface for Hermes, OpenClaw, Claude
  Desktop, or any future MCP-compatible agent
- Optional local LLM summarization/action-item extraction, routed through
  the same local/BYOK provider abstraction, never required for core use
- Local PII/redaction pass before export or share
- Custom vocabulary / word-boosting where the active ASR backend supports it
- Multi-track diarization timeline view

### Phase 2.5+ — Platform depth
- iOS parity (once the Phase 1.5 system-audio and Phase 1/1.5 Android
  learnings de-risk it)
- Share-sheet integration ("Transcribe with Scriber" on any audio/video
  file or voice memo)
- Self-hosted sync support (WebDAV/Nextcloud/Syncthing — never a
  first-party server)

## 8. Architecture summary (see ADRs for full reasoning)

- **App framework:** .NET MAUI, C#/XAML (ADR-0001)
- **UI styling:** native XAML + MAUI's CSS styling subset; SkiaSharp for
  waveform/timeline visuals; BlazorWebView reserved for narrow,
  self-contained embedded visualizations only (ADR-0002)
- **On-device inference runtime:** sherpa-onnx primary, ONNX Runtime
  direct as fallback, Whisper.net as an optional pluggable backend
  (ADR-0003)
- **Diarization model:** NVIDIA Nemotron 3 Diarization, treated as
  language-agnostic and validated empirically per-language (ADR-0004)
- **ASR strategy:** pluggable `IAsrBackend`, quantized-by-default,
  curated per-language recommendations (ADR-0005)
- **Mobile feasibility:** INT8 quantization as the default for every
  shipped model; NNAPI/CoreML delegates where available (ADR-0006)
- **AI providers:** Local (default) + BYOK only; no first-party hosting
  (ADR-0007)
- **Agent integration:** MCP server, not bespoke per-framework code
  (ADR-0008)
- **Model distribution:** manifest-driven, redirect to original sources,
  never project-hosted (ADR-0009)
- **Speaker identity:** per-session diarization (core) + optional
  cross-session voiceprint enrollment (v2, separate model) (ADR-0010)
- **Platform scope:** Android + Desktop required for v1, iOS deferred
  (ADR-0011)
- **Rust:** not the application language; reserved for a narrowly-scoped
  audio-DSP crate only if profiling later demands it (ADR-0012)

## 9. Suggested repo structure

```
scriber/
├── src/
│   ├── Scriber.App/                    # MAUI app project (UI, pages, viewmodels)
│   ├── Scriber.Core/                   # Domain models: Transcript, Speaker, Segment, Word
│   ├── Scriber.Pipeline/               # Orchestration: import → VAD → diarize → ASR → align → normalize
│   ├── Scriber.Inference.Diarization/  # Nemotron 3 Diarization wrapper
│   ├── Scriber.Inference.Asr/          # IAsrBackend + built-in implementations
│   ├── Scriber.Storage/                # SQLite store, model cache manager
│   ├── Scriber.Export/                 # TXT/SRT/VTT/Markdown/DOCX/PDF/JSON exporters
│   └── Scriber.Mcp/                    # MCP server (Phase 2)
├── models/                             # Manifest only — never weights themselves
├── docs/                               # This bundle
├── THIRD_PARTY_LICENSES.md
├── CONTRIBUTING.md
└── README.md
```
