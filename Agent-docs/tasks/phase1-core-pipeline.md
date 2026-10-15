# Phase 1 — Core Pipeline Tasks

Goal of this phase: the actual audio-in, speaker-labeled-transcript-out
pipeline, as a set of libraries independent of any UI. All tasks here
depend, directly or indirectly, on Phase 0 findings.

---

### PH1-CORE-001 — Define Scriber.Core domain models

**Status:** Done
**Depends On:** None (may start once Phase 0 is substantially complete)
**Related ADR(s):** None directly

**Goal:** Establish the shared domain types every other Phase 1 task
builds on, so no two tasks invent conflicting shapes independently.

**Scope:** New project `Scriber.Core`, no external dependencies beyond
the .NET base class library.

**Interface / Contract:**
```csharp
public record Transcript(Guid Id, string Title, DateTimeOffset CreatedAt, string LanguageCode, IReadOnlyList<Speaker> Speakers, IReadOnlyList<Segment> Segments, AudioReference Audio);
public record Speaker(string Id, string DisplayName, string ColorHex);
public record Segment(string SpeakerId, TimeSpan Start, TimeSpan End, IReadOnlyList<Word> Words);
public record Word(string Text, TimeSpan Start, TimeSpan End, double Confidence);
public record AudioReference(string LocalPath, TimeSpan Duration, int SampleRate);
```
(Exact field list may be refined during implementation, but the shape —
Transcript owns Speakers and Segments, Segment owns Words, Word carries
confidence — is fixed and load-bearing for every downstream task.)

**Acceptance Criteria:**
- All types compile with no dependency beyond the BCL.
- Unit tests cover construction and basic invariants (e.g., a Segment's
  Words must fall within its Start/End range).

**Out of Scope:** Persistence (see `phase1-storage-export.md`), any
inference logic.

**Suggested Verification:** Unit tests in `Scriber.Core.Tests`.

**Completion note (2026-10-06):** Done. `src/Scriber.Core` — Transcript, Speaker, Segment,
Word, AudioReference (classic records with validating constructors; the task's positional-record
contract shape preserved: same construction signatures, `with` support, value equality) plus
`AudioBuffer` (interleaved float32 PCM, the shared currency of every pipeline stage). Enforced
invariants: Segment words inside [Start, End] and chronological; End > Start; Word confidence in
[0,1]; Speaker.ColorHex #RRGGBB; every Segment.SpeakerId resolves to a declared Speaker.
Verified: `dotnet test src/Scriber.Core.Tests` — 12/12 passed.

---

### PH1-CORE-002 — Audio import & decode to normalized PCM

**Status:** Done
**Depends On:** PH1-CORE-001
**Related ADR(s):** None directly

**Goal:** Accept common audio/video container formats and produce a
normalized 16kHz mono PCM buffer that every downstream model expects.

**Scope:** `Scriber.Pipeline/Import`.

**Acceptance Criteria:**
- Given mp3, wav, m4a, and mp4 (audio track extraction) inputs, produces
  a 16kHz mono PCM buffer.
- Unit tests cover at least 3 distinct input formats with known-good
  fixture files.
- Gracefully rejects unsupported/corrupt files with a clear error, rather
  than throwing an unhandled exception into the pipeline.

**Out of Scope:** VAD (next task), any UI file-picker (see
`phase1-ui-shell.md`).

**Suggested Verification:** Unit tests with fixture audio files of each supported format.

**Completion note (2026-10-06):** Done. `src/Scriber.Pipeline/Import` — `IAudioDecoder`
(`Task<AudioBuffer> DecodeAsync(string, CancellationToken)`) with `FfmpegAudioDecoder` (local
ffmpeg, resolved via SCRIBER_FFMPEG env var then PATH; outputs 16 kHz mono f32le → `AudioBuffer`;
clear `AudioDecodeException` for corrupt/unsupported/no-audio-stream inputs, including ffmpeg's
"does not contain any stream" case normalized to plain language). Committed fixtures: tone.wav,
tone.mp3, tone.m4a, tone.mp4 (1.0 s 1 kHz sine @ 1/8 amp + 0.5 s silence, 16 kHz mono —
measured decode RMS ≈ 0.088 in-tone, 0.0 in pad), videoonly.mp4, notes.txt.
Verified: `dotnet test src/Scriber.Pipeline.Tests` — 8/8 passed (all 4 formats, level checks,
corrupt/unsupported/video-only rejection).

---

### PH1-CORE-003 — VAD pre-pass integration

**Status:** Done
**Depends On:** PH1-CORE-002
**Related ADR(s):** ADR-0003

**Goal:** Trim silence from the normalized PCM buffer before the more
expensive diarization/ASR passes run.

**Scope:** `Scriber.Pipeline`, wrapping sherpa-onnx's VAD (per
PH0-SPIKE-001's runtime findings).

**Acceptance Criteria:**
- Given a PCM buffer with deliberate silence gaps (fixture file), returns
  a list of speech-only segment ranges with silence excluded.
- Does not clip the start/end of actual speech (verify with a fixture
  that has speech starting very close to a silence boundary).

**Out of Scope:** Diarization, ASR.

**Suggested Verification:** Unit test asserting returned segment
boundaries against a known-good fixture.

**Completion note (2026-10-15):** Done. `src/Scriber.Pipeline/Vad` — `IVad`, `SherpaVad`,
`VadOptions`, and `SpeechRange` wrapping sherpa-onnx Silero VAD (v5). Audio is fed in
`WindowSize` chunks (512 samples / 32 ms) so sherpa evaluates speech independently per
window rather than or-ing speech across large buffers. Verified on `two_speaker_fixture.wav`
(8/8 speech turns recovered, 0.5 s silence gaps excluded), `silence.wav` (0 ranges), and
sample rate / channel validation. `dotnet test src/Scriber.Pipeline.Tests` — 13/13 passed.

---

### PH1-CORE-004 — Diarization integration

**Status:** Done
**Depends On:** PH1-CORE-003, PH0-SPIKE-001, PH0-SPIKE-002
**Related ADR(s):** ADR-0003, ADR-0004

**Goal:** Wrap Nemotron 3 Diarization behind a clean interface, using
whichever runtime path PH0-SPIKE-001 established as correct.

**Scope:** New project `Scriber.Inference.Diarization`.

**Interface / Contract:**
```csharp
public interface IDiarizer
{
    Task<IReadOnlyList<SpeakerTurn>> DiarizeAsync(AudioBuffer audio, DiarizationOptions options, CancellationToken ct);
}
public record SpeakerTurn(string SpeakerId, TimeSpan Start, TimeSpan End);
```

**Acceptance Criteria:**
- Given the VAD-trimmed PCM buffer, returns speaker turns with plausible
  start/end/speakerId values on the PH0-SPIKE-002/003 test set, consistent
  with the spike findings.
- Supports both offline and streaming operating points via
  `DiarizationOptions` (even if streaming is only exercised later in
  `phase1.5-recording-streaming.md`).
- Speaker IDs are stable and ordered by first appearance within one call.

**Out of Scope:** ASR, cross-session speaker identity (ADR-0010, Phase 2).

**Suggested Verification:** Integration test against the PH0-SPIKE-002
fixture audio, asserting turn count and rough boundaries within a defined
tolerance.

**Completion note (2026-10-15):** Done. `src/Scriber.Inference.Diarization` — `IDiarizer`,
`NemotronDiarizer`, `DiarizationOptions`, `SpeakerTurn` implementing on-device diarization
via raw `Microsoft.ML.OnnxRuntime` (`nemotron3_stream_step.onnx`) with native Mel frontend,
feature-stacking `Embedder`, and host-side speaker FIFO cache (`SpeakerCache`) ported from
spike 002. Tested on `two_speaker_fixture.wav` (recovers 8/8 turns, stable appearance-ordered
speaker IDs `speaker-0` / `speaker-1`, turn durations $\ge 3\text{ s}$, matching spike 002
boundaries within 0.25 s tolerance). `dotnet test src/Scriber.Inference.Diarization.Tests` — 6/6 passed.

---

### PH1-CORE-005 — Define IAsrBackend and first concrete backend

**Status:** Done
**Depends On:** PH1-CORE-002, PH0-SPIKE-002
**Related ADR(s):** ADR-0005

**Goal:** Establish the pluggable ASR interface (this is a load-bearing
contract for many later tasks — see `00-task-conventions.md`'s
cross-task interface discipline note) and implement the first concrete
backend.

**Scope:** New project `Scriber.Inference.Asr`.

**Interface / Contract:** exactly the `IAsrBackend` interface specified
in ADR-0005 — do not diverge from that signature without a superseding
ADR.

**Acceptance Criteria:**
- `TranscribeAsync` returns word-level segments with timestamps and a
  per-word confidence value, given the VAD-trimmed PCM buffer.
- First concrete implementation wraps whichever multilingual model
  PH0-SPIKE-002 validated.
- `SupportedLanguageCodes` and `SupportsStreaming` are accurately
  reported (do not hardcode `true` for streaming if the backend doesn't
  actually support it yet).

**Out of Scope:** The curated Persian-specific backend (separate task,
`PH1-MODEL-004`, once PH0-SPIKE-003 names a winner); streaming
implementation body (stub acceptable here, real implementation in
`phase1.5-recording-streaming.md`).

**Suggested Verification:** Integration test against PH0-SPIKE-002/003
fixtures, asserting non-empty, plausible transcript output with
confidence values in `[0,1]`.

**Completion note (2026-10-15):** Done. `src/Scriber.Inference.Asr` — `IAsrBackend`
(exact signature per ADR-0005: `Id`, `DisplayName`, `SupportedLanguageCodes`, `SupportsStreaming`,
`TranscribeAsync`, `TranscribeStreamingAsync`), `TranscriptSegment`, `PartialTranscriptSegment`,
`AudioChunk`, and `AsrOptions`. `SherpaWhisperAsrBackend` wraps multilingual Whisper-small INT8 via
`sherpa-onnx` (`small-encoder.int8.onnx`, `small-decoder.int8.onnx`, `small-tokens.txt`), with audio windowing
for cuts $> 28\text{ s}$ and word-level timestamp/confidence extraction. `SupportsStreaming` accurately reports
`false` (throws `NotSupportedException` on streaming call). Verified against `two_speaker_fixture.wav`:
extracts words with chronological order, confidence in $[0, 1]$, and recognized ground-truth vocabulary.
`dotnet test src/Scriber.Inference.Asr.Tests` — 6/6 passed.

---

### PH1-CORE-006 — Alignment / merge pass

**Status:** Done
**Depends On:** PH1-CORE-004, PH1-CORE-005
**Related ADR(s):** ADR-0004, ADR-0005

**Goal:** Merge diarization turns and ASR word output into a single
ordered, speaker-attributed transcript.

**Scope:** `Scriber.Pipeline/Alignment`.

**Acceptance Criteria:**
- Produces an ordered `IReadOnlyList<Segment>` (per `PH1-CORE-001`'s
  domain model) where every `Word` is attributed to the `SpeakerTurn`
  with greatest time overlap.
- A word spanning a speaker-turn boundary is explicitly handled (midpoint
  split is the documented v1 behavior — record this choice in code
  comments referencing this task).
- Overlapping speech (both models may represent it) has a documented,
  explicit v1 behavior (e.g., assign to the dominant/first turn) — this
  is a known, intentional v1 simplification, not an oversight, and must
  be called out as such in a code comment and in this task's completion
  note.
- Unit tests cover: no boundary overlap (simple case), a word crossing a
  boundary, and an overlapping-speech fixture.

**Out of Scope:** Improving the overlapping-speech handling beyond the
documented v1 simplification — track any such improvement as a new,
separate task if pursued later.

**Suggested Verification:** Unit tests as described above.

**Completion note (2026-10-15):** Done. `src/Scriber.Pipeline/Alignment` — `ITranscriptAligner`
and `TranscriptAligner` attributing words to `SpeakerTurn`s by maximum time overlap.
Boundary words spanning turns use majority duration overlap (which implements midpoint split for
adjacent turns). For overlapping speech, when turns share overlap with a word, the turn with greatest
overlap duration wins, and ties are broken in favor of the earlier-starting (dominant/first) turn;
this is an explicit documented v1 simplification called out in code and here. Verified: unit tests in
`Scriber.Pipeline.Tests.AlignmentTests` covering simple boundary cases, boundary-crossing words,
overlapping-speech ties/dominance, silence gaps, and `TranscriptSegment` domain invariants.
`dotnet test src/Scriber.Pipeline.Tests` — 20/20 passed.

---

### PH1-CORE-007 — Persian-aware text normalization pass

**Status:** Done
**Depends On:** PH1-CORE-006
**Related ADR(s):** ADR-0005

**Goal:** Clean up raw ASR/alignment output for readability without
corrupting Persian-specific text features.

**Scope:** `Scriber.Pipeline/Normalization`.

**Acceptance Criteria:**
- Zero-width non-joiner (ZWNJ) characters in Persian compound words (e.g.
  می‌خواهم) are preserved through the full pipeline, verified by a
  round-trip unit test on fixture strings containing ZWNJ.
- A user-configurable setting controls Persian-script vs. Latin numeral
  rendering in transcript body text (timestamps in UI chrome are a
  separate concern, handled in `phase1-ui-shell.md`).
- Light punctuation cleanup does not alter word content or timestamps.

**Out of Scope:** Full grammatical correction, translation.

**Suggested Verification:** Unit tests with Persian fixture strings
containing ZWNJ-joined compounds, mixed numeral styles, and standard
punctuation.

**Completion note (2026-10-15):** Done. `src/Scriber.Pipeline/Normalization` — `ITextNormalizer`,
`PersianTextNormalizer`, `NormalizationOptions`, `NumeralStyle`. Preserves ZWNJ (`\u200C`) in Persian
compounds (می‌خواهم, خانه‌ها, کتاب‌هایم, رفته‌اند) without truncation or stripping; standardizes
Arabic Yeh/Kaf to standard Persian ی / ک; provides configurable `NumeralStyle` (Persian `۰-۹`, Latin `0-9`,
Unchanged); cleans whitespace around punctuation (،, ؟, ؛, ., !) without altering word content,
start/end timestamps, or confidence scores. Unit tests in `Scriber.Pipeline.Tests.NormalizationTests`
verified all invariants. `dotnet test src/Scriber.Pipeline.Tests` — 28/28 passed.
