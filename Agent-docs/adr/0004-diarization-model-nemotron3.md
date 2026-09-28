# ADR-0004: Diarization Model — NVIDIA Nemotron 3 Diarization

**Status:** Accepted

## Context

Scriber needs a "who spoke when" model: small, fast, capable of both
offline (highest-accuracy) and streaming (low-latency, live) operation,
and usable across many languages including Persian.

## Decision

Adopt **nvidia/Nemotron-3-Diarization** (OpenMDW-1.1 license, permissive
for commercial and non-commercial use) as the sole diarization model.

Treat it as **language-agnostic by architecture**: it is a frame-level
acoustic classifier over Mel-spectrogram features, predicting per-speaker
activity with no text, vocabulary, or language-specific component at all
— it never "understands" any language, which is exactly why it
generalizes across them. This is a stronger, more durable property than
"it was trained on N languages."

This does **not** mean assuming equal accuracy on every language without
checking. Its published Diarization-Bench numbers (14.72% DER) are not
Persian-specific. **Empirical validation on real Persian conversational
audio is required** (`PH0-SPIKE-003`) before relying on it for the
Persian launch experience.

## Consequences

- Diarization and transcription remain two separate model problems that
  must be explicitly merged (see `PH1-CORE-006`, the alignment pass) —
  this is a permanent architectural property of the pairing, not a
  temporary gap.
- 100M parameters, ~400MB fp32 / ~100MB int8 — small enough that GPU-only
  claims found in some secondary sources are a benchmark-hardware
  artifact of NVIDIA's reference PyTorch/NeMo runtime, not a hard
  requirement of the architecture itself (see ADR-0006).
- Supports 1–8 speakers including overlapping speech, and both offline
  (30.4s context) and streaming (down to 0.32s latency) modes from the
  same checkpoint via parameter changes — directly enables the two-pass
  live/final design in Flow B of the product spec.

## Alternatives considered

No serious alternative diarizer was evaluated in depth, since Nemotron 3
Diarization's small size, dual offline/streaming support from one
checkpoint, and permissive license made it a strong first choice; this
ADR should be revisited only if `PH0-SPIKE-003`/`PH0-SPIKE-004` reveal a
Persian-accuracy or on-device-performance problem specific to this model.
