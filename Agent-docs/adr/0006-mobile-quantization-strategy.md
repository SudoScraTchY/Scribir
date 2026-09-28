# ADR-0006: Mobile Feasibility & Quantization Strategy

**Status:** Accepted

## Context

An early concern was that Nemotron 3 Diarization's association with "4GB
VRAM" in some secondary sources implied a hard GPU requirement, which
would rule out smartphone-only operation. Research established this
figure reflects NVIDIA's reference benchmark/training hardware (used for
high-throughput server workloads), not an architectural floor: the model
is 100M parameters (~400MB fp32, ~100MB int8), and comparable-sized peer
models (quantized Whisper-tiny/base, similar FastConformer variants) are
already shipping today at 40–100MB footprints via ONNX Runtime Mobile or
tflite, CPU-only, on real phones.

## Decision

- **Every downloadable model ships an INT8-quantized variant as the
  default** in the model manifest (ADR-0009); fp16/fp32 is an explicit,
  opt-in "I have a capable device and want maximum accuracy" toggle.
- Use **ONNX Runtime Mobile / sherpa-onnx's NNAPI (Android) and CoreML
  (iOS, when built) execution providers** where available, falling back
  cleanly to CPU-only inference.
- Treat **live-streaming latency and battery drain on low-end devices**,
  not raw feasibility, as the real open risk — to be settled by actual
  on-device measurement (`PH0-SPIKE-004`), never assumed either way.

## Consequences

- The Model Manager and first-run benchmark (`phase1-model-manager.md`)
  must always default to the quantized variant and only offer full-
  precision as an explicit opt-in, never the reverse.
- `product/03-model-device-matrix-template.md` exists specifically to
  hold the real, measured numbers this decision depends on — this ADR's
  "should be fine" reasoning is a hypothesis to confirm, not a substitute
  for measurement.

## Alternatives considered

- **Assume GPU/desktop-only for v1, defer mobile inference entirely:**
  Rejected given Android is a required v1 platform (ADR-0011) and the
  peer-model evidence that CPU-only quantized inference at this model
  size is already a solved, shipping pattern elsewhere.
