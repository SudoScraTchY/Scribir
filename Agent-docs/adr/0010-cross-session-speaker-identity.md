# ADR-0010: Cross-Session Speaker Identity via Separate Voice-Embedding Enrollment

**Status:** Accepted (Phase 2 — not required for v1 MVP)

## Context

Nemotron 3 Diarization (ADR-0004) resolves speakers only *within one
recording*, by arrival order — it has no concept of "this is the same
person from last Tuesday's recording." Recurring use cases (a standing
team meeting, a recurring class, a returning therapy client) get no
benefit today from having transcribed the same people before. This was
identified as the single largest unbuilt feature gap versus the existing
competitive landscape (`product/02-motives-and-differentiators.md`).

## Decision

Add a **separate, optional** speaker-embedding/verification model
(ECAPA-TDNN or Titanet class — available via sherpa-onnx per ADR-0003)
running *alongside*, not instead of, the diarizer:

1. **Enrollment:** capture one calibration sentence per named participant
   (or harvest a clean 3–5s segment from their first turn in a session),
   extract an embedding, store it locally under the given name.
2. **Matching:** on future sessions, compare each diarized speaker
   segment's embedding against the stored voiceprint library; above a
   confidence threshold, suggest the matching name.
3. **Never silent:** every suggested match is shown with a visible
   confidence indicator and a one-tap override/correct — this must never
   silently auto-assign an identity without the option to fix it.

## Consequences

- This is explicitly a **v2, opt-in** feature, scoped separately from
  core per-session diarization, which remains fully functional without
  any enrollment ever happening.
- Requires its own local storage (named embeddings) and its own
  management UI (rename/delete/merge two mistakenly-separate profiles —
  `PH2-SPEAKER-004`).
- Voiceprint data is sensitive personal-identity data; it must stay
  local, covered by the same at-rest-encryption option as the transcript
  library (`PH1-STORE-003`), and never leave the device as part of any
  BYOK or MCP flow unless the user explicitly exports it.

## Alternatives considered

- **No cross-session identity at all:** Rejected — this was identified as
  the strongest available differentiator versus existing tools, and the
  underlying capability (speaker embeddings) is already available through
  the chosen runtime (ADR-0003) at low additional integration cost.
