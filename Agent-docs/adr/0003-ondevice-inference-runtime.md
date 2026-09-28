# ADR-0003: On-Device Inference Runtime — sherpa-onnx primary

**Status:** Accepted (with one open sub-question flagged for Phase 0)

## Context

Both core models (Nemotron 3 Diarization and whichever ASR backend is
active, ADR-0004/0005) started life as PyTorch/NeMo models and must run
from C#, fully offline, across Windows/macOS/Android (and later iOS).
Three realistic paths exist: raw `Microsoft.ML.OnnxRuntime`, the
`sherpa-onnx` toolkit (which wraps ONNX Runtime and already exposes VAD,
ASR, diarization, and speaker-embedding as first-class C#-bound
functions), or `Whisper.net` (a whisper.cpp binding) for the ASR half
specifically.

## Decision

Use **sherpa-onnx**, via its C# bindings, as the primary integration
layer for VAD, ASR, diarization, and (later, ADR-0010) speaker embedding.
Treat raw `Microsoft.ML.OnnxRuntime` as the fallback/upgrade path for
anything sherpa-onnx cannot express (for example, very specific control
over Nemotron 3 Diarization's streaming speaker-cache/FIFO parameters).
Treat `Whisper.net` as one *optional* pluggable `IAsrBackend`
implementation (ADR-0005), not the primary runtime.

## Alternatives considered

- **Raw ONNX Runtime for everything:** Maximum control, but means hand-
  writing Mel-spectrogram feature extraction and streaming buffer/FIFO
  management in C# from NVIDIA's reference implementation — real, non-
  trivial engineering effort that sherpa-onnx already provides.
- **Whisper.net for everything:** Excellent, MAUI-exercised binding for
  Whisper-family ASR specifically, but does not do diarization — would
  still need to sit alongside a second runtime for that half.

## Open question — requires a Phase 0 spike before this ADR is fully closed

It is not yet confirmed whether sherpa-onnx's C# diarization API can
accommodate a **custom ONNX export of Nemotron 3 Diarization** with its
specific streaming speaker-cache parameters, as opposed to only the
diarization models sherpa-onnx ships with by default. `PH0-SPIKE-001`
exists specifically to answer this before any product-layer code is
written against an assumed API shape.

## Consequences

- Significantly less bespoke feature-extraction/streaming-state code to
  write and maintain versus raw ONNX Runtime.
- Dependency on upstream sherpa-onnx's packaging and platform coverage
  (currently: Android/iOS/Windows/macOS/Linux, x64/x86/arm64/arm32).
- If `PH0-SPIKE-001` finds sherpa-onnx cannot express Nemotron's
  streaming parameters, fall back to raw ONNX Runtime for the diarizer
  specifically while keeping sherpa-onnx for VAD/ASR/embedding — this is
  an acceptable, anticipated partial-fallback outcome, not a reason to
  abandon sherpa-onnx entirely.
