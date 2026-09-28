# ADR-0001: Application Framework — .NET MAUI

**Status:** Accepted

## Context

Scriber needs one codebase spanning Android and Desktop (Windows/macOS)
for v1, with iOS possible later (ADR-0011). The app will be built largely
by an autonomous coding agent, with a human maintainer (professional
C#/.NET background, ~5 years) reviewing and unblocking it. Inference
itself (diarization + ASR) will run through a native or llama.cpp-class
runtime regardless of the app language (ADR-0003), so the app language's
own runtime speed is not the deciding factor.

## Decision

Build the application in **.NET MAUI (C#/XAML)**.

## Alternatives considered

- **Rust via Dioxus:** Mobile support is real but explicitly described by
  Dioxus's own documentation as experimental on Android; a March 2026
  community report showed `dx serve --ios --device` working for one
  developer and crashing for another in the same thread. Rust's ownership
  model also means an agent's generated code fails to *compile* more
  often before it fails at *runtime* — a slower iteration loop for
  agent-driven development. Native/llama.cpp interop is a genuine Rust
  strength, but that's decoupled from the app-language choice (ADR-0003).
- **Flutter (Dart):** Had the most mature *native-inference* story of any
  option evaluated — multiple actively maintained `dart:ffi` bindings to
  llama.cpp/whisper.cpp, including a Snapdragon Hexagon NPU-accelerated
  variant already shipped. Mature, battle-tested RTL support. Rejected
  primarily because it introduces a third language (Dart) the human
  maintainer has no professional fluency in, reducing their ability to
  personally debug agent-generated output — judged more important than
  Flutter's inference-ecosystem edge for an agent-built project.
- **Kotlin Multiplatform:** Stable and proven at scale for shared
  *business logic*, but UI is not shared unless Compose Multiplatform is
  also adopted, which is materially less mature than the logic-sharing
  story, especially on iOS. Would require maintaining two native UI
  layers — more total surface area than MAUI/Flutter/Dioxus's "one UI,
  all platforms" model, working against the "one agent, one codebase"
  efficiency this project depends on.

## Consequences

- Accept that .NET 11 (current RC1 as of this writing, GA expected
  November 2026) has drawn real, current community criticism for
  regressions and breaking changes across .NET 8→9→10 upgrade cycles —
  the floor is more furnished than Dioxus's but not perfectly calm.
- Gain MAUI's much larger plugin/library ecosystem for the boring-but-
  essential parts (mic permissions, file pickers, background audio) and,
  critically, a human maintainer who can personally read, debug, and
  correct agent-generated C#.
- .NET 11's Runtime Async feature (runtime-native async/await, confirmed
  in Microsoft's own RC1 release notes) is directly relevant given
  Scriber's pipeline is fundamentally a long-running async/streaming
  workload — worth targeting once GA, not a reason to delay on preview
  instability.

## Revisit trigger

If real on-device mobile benchmarking (`PH0-SPIKE-004`) shows a
performance or battery problem specifically attributable to MAUI/C#
overhead — not to model size or quantization choices — that Flutter's
NPU-accelerated native-inference path would solve, revisit this decision
with a new ADR rather than patching around it indefinitely.
