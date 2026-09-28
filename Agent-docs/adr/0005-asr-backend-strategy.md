# ADR-0005: ASR Backend Strategy — Pluggable, Quantized-by-Default, Curated Per Language

**Status:** Accepted

## Context

Unlike diarization (ADR-0004), transcription is genuinely language-
dependent, and no single ASR model is best across all languages, device
tiers, and accuracy/speed trade-offs. Candidate families identified during
research: multilingual Whisper (and community fine-tunes), wav2vec2-XLSR-
Persian (~10.4% WER on Common Voice Persian in community reporting),
community NeMo FastConformer-based Persian streaming models, and NVIDIA's
own Nemotron 3.5 ASR (broad multilingual, cache-aware streaming).

## Decision

Define a single interface, implemented by multiple swappable backends:

```csharp
public interface IAsrBackend
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyList<string> SupportedLanguageCodes { get; }
    bool SupportsStreaming { get; }
    Task<TranscriptSegment[]> TranscribeAsync(AudioBuffer audio, AsrOptions options, CancellationToken ct);
    IAsyncEnumerable<PartialTranscriptSegment> TranscribeStreamingAsync(IAsyncEnumerable<AudioChunk> chunks, AsrOptions options, CancellationToken ct);
}
```

Ship with:
- A **default "auto" multilingual model** (a mid-size Whisper tier) so
  the app works reasonably for any language out of the box.
- A **curated, per-language "recommended" model**, chosen only after a
  head-to-head internal benchmark on real audio (not published benchmark
  numbers alone) — Persian is the first language to receive this
  treatment (`PH0-SPIKE-003`).
- Support for **community-contributed backends/models** via the model
  manifest (ADR-0009), so language coverage grows without requiring a
  core rewrite.

## Consequences

- Adding a new language becomes "find, evaluate, and manifest-register a
  model," not an engineering rewrite.
- Requires maintaining a small internal benchmark set per priority
  language, starting with Persian, to make "recommended" claims honestly
  (feeds `product/03-model-device-matrix-template.md`).
- The alignment pass (`PH1-CORE-006`) must be backend-agnostic, since
  different backends may return word-level vs. segment-level timestamps.

## Alternatives considered

- **Hard-coding one ASR model:** Rejected — no single model serves both
  "low-end phone, any language" and "workstation, best possible Persian
  accuracy" well, and it forecloses the community-contribution growth
  path central to the project's language-coverage goals.
