# ADR-0012: Rust Scope — Not the Application Language; Reserved for an Isolated DSP Crate Only If Needed

**Status:** Accepted

## Context

Rust was evaluated twice during planning: first as a candidate
*application* language (via Dioxus, see ADR-0001), and separately for its
potential performance benefit in the audio hot path specifically
(feature extraction, ring buffers, resampling, real-time audio
callbacks), given inference itself runs through native/llama.cpp
regardless of app language (ADR-0003).

## Decision

Rust is **not** used as Scriber's application language (MAUI/C#, per
ADR-0001). It remains a legitimate, narrowly-scoped option for **one
specific, isolated slice**: a small Rust crate handling audio DSP (Mel-
spectrogram feature extraction, ring-buffer/streaming-buffer management)
exposed to the MAUI app via a thin, stable C-ABI/FFI boundary — adopted
**only if** on-device profiling during Phase 1.5 shows GC-pause-driven
audio glitches during live capture that C#'s `Span<T>`/`ArrayPool<T>`
cannot resolve.

This is a conditional, evidence-gated option, not a planned Phase 1/1.5
task. No task in this bundle currently assumes Rust will be introduced.

## Consequences

- Avoids doubling the build toolchain, FFI surface, and cross-compilation
  matrix (4+ target triples per platform) for an agent-built project
  before there is concrete evidence it's needed.
- Keeps the option open cheaply: if `PH1.5-REC-002`'s streaming state-
  management work reveals real audio-callback jitter attributable to GC
  pauses, this ADR's condition for introducing Rust is already pre-
  approved and scoped, avoiding a mid-project architecture debate.

## Alternatives considered

- **Rust as the full application language (Dioxus):** Addressed and
  rejected in ADR-0001.
- **Rust for the whole audio pipeline, decided upfront:** Rejected as
  premature — "performance" was not established as the actual bottleneck
  anywhere in the pipeline; model size, quantization (ADR-0006), and
  correct streaming-state management are the real levers, and none of
  those require leaving C#.
