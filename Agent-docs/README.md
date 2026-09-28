# Scriber — Technical Specification & Agent Task Bundle

This is the full engineering specification for **Scriber** (working name), an
open-source, non-monetized, offline-first, cross-platform speaker-attributed
transcription app built on .NET MAUI. It is written to be handed to an
autonomous coding agent (Hermes, or any other MCP/ACP-compatible agent) as
its primary source of truth, alongside a human maintainer.

## How this bundle is organized

```
scriber-docs/
├── README.md                          — this file
├── product/
│   ├── 01-product-specification.md    — what we're building, for whom, and the full feature set by phase
│   ├── 02-motives-and-differentiators.md — why it's worth building, the competitive landscape, and what's genuinely new
│   └── 03-model-device-matrix-template.md — living template for device-tier × model recommendations (to be filled in by real testing, not guessed)
├── adr/
│   └── 0001 … 0012                    — one Architecture Decision Record per locked-in technical decision
└── tasks/
    ├── 00-task-conventions.md         — the ticket format every task file below follows
    ├── phase0-spike.md                — de-risking spikes, before any product code is written
    ├── phase1-core-pipeline.md        — audio → diarization → ASR → alignment → normalization
    ├── phase1-storage-export.md       — local storage, library/search, all export formats
    ├── phase1-ui-shell.md             — the MAUI app itself: import, transcript view, RTL, waveform
    ├── phase1-model-manager.md        — model manifest, download flow, first-run device benchmark
    ├── phase1.5-recording-streaming.md — mic capture, live streaming transcription, system-audio investigation
    ├── phase2-speaker-identity.md     — cross-session voiceprint enrollment and matching
    └── phase2-integrations-mcp-provider-redaction.md — BYOK, MCP server, summarization, PII redaction, diff view
```

## Reading order for a new agent

1. Read this file.
2. Read **all of `adr/`** before touching any task. The ADRs are the
   decisions already made, with the reasoning behind them — do not
   re-litigate them inside a task. If a task's implementation reveals that
   an ADR should change, stop and write a new ADR proposal rather than
   silently deviating (see `tasks/00-task-conventions.md`).
3. Read `product/01-product-specification.md` for the shape of the whole
   product, then `product/02-motives-and-differentiators.md` for *why* — a
   few tasks (confidence-heatmap styling, structured Markdown export, the
   device benchmark harness) only make sense with that context.
4. Read `tasks/00-task-conventions.md` for the ticket format, then work
   the phase files in order: `phase0-spike` must be substantially complete
   (or its findings otherwise available) before `phase1-*` begins, since
   several Phase 1 tasks depend directly on spike findings.

## Standing constraints that apply to every task in this bundle

- **No first-party model hosting, no paid API reselling, ever.** Model
  weights are never bundled or proxied through project infrastructure —
  see ADR-0009. This is a product identity constraint, not a temporary
  cost-saving measure.
- **Fully offline must always remain possible.** BYOK and any agent/MCP
  integration are additive, opt-in ceiling-raisers — never a requirement
  for core transcription to work (ADR-0007).
- **Android and Desktop (Windows/macOS) are both required for v1.** iOS is
  explicitly deferred (ADR-0011).
- **ADRs are immutable once accepted.** They are superseded by a new,
  numbered ADR that references the one it replaces — never edited in
  place.

## Status

This bundle reflects the product and architecture discussion as of the
current planning conversation. It is a living document set: the manifest
in `product/03-model-device-matrix-template.md` is intentionally empty of
real numbers until Phase 0 benchmarking produces them — do not fill it
with guessed figures.
