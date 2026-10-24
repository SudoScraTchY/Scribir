# Phase 1.5 — Recording & Live Streaming Tasks

---

### PH1.5-REC-001 — Microphone capture

**Status:** Complete
**Depends On:** PH1-CORE-002
**Related ADR(s):** None directly

**Goal:** Cross-platform microphone audio capture within the MAUI app.

**Scope:** `Scriber.App` (platform-specific audio-capture glue as
needed), feeding into `Scriber.Pipeline`'s existing normalized-PCM
expectations from `PH1-CORE-002`.

**Acceptance Criteria:**
- Captures microphone audio on both Android and Desktop (Windows/macOS)
  targets, with correct permission-request flows on each.
- Captured audio matches the 16kHz mono PCM shape the rest of the
  pipeline expects, without requiring a separate re-encode step.

**Implementation Notes:**
- Added `WavHelper` in `Scriber.Core.Audio` providing direct header writing, fast span-based RMS VU metering, and zero-FFmpeg WAV decoding.
- Added `IAudioRecorder` interface with event hooks (`AudioLevelChanged`, `ElapsedTimeChanged`, `StateChanged`).
- Implemented `AndroidAudioRecorder` using `Android.Media.AudioRecord` on `AudioSource.VoiceRecognition` (16kHz mono 16-bit PCM) with `ArrayPool<byte>.Shared` streaming to disk.
- Implemented `WindowsAudioRecorder` utilizing `Windows.Media.Capture.MediaCapture` for 16kHz mono PCM capture.
- Configured Android permissions (`RECORD_AUDIO`, `MODIFY_AUDIO_SETTINGS`) and Windows device capability (`microphone`).

---

### PH1.5-REC-002 — Streaming diarization + streaming ASR wiring

**Status:** Complete
**Depends On:** PH1-CORE-004, PH1-CORE-005, PH1.5-REC-001
**Related ADR(s):** ADR-0004, ADR-0005, ADR-0012

**Goal:** Wire live microphone audio through the models' low-latency
streaming operating points to produce a live partial transcript.

**Scope:** `Scriber.Pipeline`, using `IDiarizer` and `IAsrBackend`'s
streaming members.

**Acceptance Criteria:**
- Partial transcript visibly updates within the diarizer's documented
  streaming latency window (as low as 0.32s per its model card,
  configuration-dependent).
- Streaming state (diarizer's speaker cache/FIFO, ASR's cache-aware
  chunking state) correctly persists across audio chunks with no
  corruption — explicitly test pause and resume (see PH1.5-REC-004) as
  part of this task's verification, not as an afterthought.

**Implementation Notes:**
- Added `ILiveTranscriptionSession` and `LiveTranscriptionSession` in `Scriber.Pipeline.Streaming`.
- Added `AudioDataAvailable` event to `IAudioRecorder` emitting zero-allocation audio chunks straight from microphone read threads.
- Wired audio chunks through `WavHelper.Pcm16ToFloat` into `LiveTranscriptionSession.PushAudioAsync`.
- Handled Pause and Resume with continuous timestamp preservation and zero state drift.
- Integrated `LiveDraftSegments` and real-time streaming card in `TranscribePage.xaml`.
- Verified with comprehensive unit tests in `LiveTranscriptionSessionTests`.

---

### PH1.5-REC-003 — Two-pass finalization

**Status:** Complete
**Depends On:** PH1.5-REC-002
**Related ADR(s):** ADR-0004

**Goal:** On stop, re-run the offline/high-accuracy model configuration
over the full recording and reconcile with the live draft, per the
two-pass design in Flow B of the product spec.

**Scope:** `Scriber.Pipeline`.

**Acceptance Criteria:**
- After recording stops, the offline-configuration pass runs in the
  background without blocking the UI.
- The finalized transcript replaces the live draft, with any user edits
  made during the live phase preserved rather than silently discarded
  (coordinate with the diff-aware design philosophy in `PH2-INT-005`,
  even though that task itself is Phase 2 — this task must not make
  future diff-preservation impossible).

**Implementation Notes:**
- Created `ITwoPassReconciler` and `TwoPassReconciler` in `Scriber.Pipeline.Streaming`.
- Matches live draft segments against offline high-accuracy segments via temporal Intersection-over-Union (IoU >= 0.3).
- Explicitly detects and preserves user edits made during the live phase, while adopting refined speaker IDs, boundaries, and confidence metrics from the offline pass.
- Verified with unit tests in `TwoPassReconcilerTests`.

---

### PH1.5-REC-004 — Pause/resume recording UI

**Status:** Complete
**Depends On:** PH1.5-REC-001
**Related ADR(s):** None directly

**Goal:** Let the user pause and resume an in-progress recording cleanly.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- Pausing stops audio capture and streaming inference without corrupting
  state (verified jointly with `PH1.5-REC-002`'s streaming-state test).
- Resuming continues the same recording session (not a new one) with
  continuous timestamps.

**Implementation Notes:**
- Segmented mode UI on `TranscribePage` supporting seamless Pause and Resume.
- Dynamic VU audio meter, status pill (`Recording...` / `Paused`), and real-time digital timer bound to `ElapsedTime`.
- Safe pause logic preserves open WAV header and file stream; resume seamlessly continues appending 16kHz mono PCM frames with continuous timestamp progression.


**Out of Scope:** Multi-segment recordings treated as separate files
(pause/resume within one session only).

**Suggested Verification:** Manual pause/resume test with timestamp
continuity check.

---

### PH1.5-REC-005 — System-audio loopback capture investigation (spike, Windows/macOS only)

**Status:** Done
**Completed:** 2026-10-20
**Depends On:** None
**Related ADR(s):** ADR-0011

**Goal:** Determine feasibility of capturing arbitrary system audio (a
browser call, a video actually playing, a podcast app) as a live, unified
capture mode — the "flagship" differentiator described in
`product/02-motives-and-differentiators.md` §3.6. Per ADR-0011, this is
explicitly scoped to Windows and macOS only; Android/iOS system-audio
loopback is out of scope for this task and for v1 generally.

**Scope:** A platform-specific proof of concept per OS — WASAPI loopback
on Windows, ScreenCaptureKit (or a documented virtual-device approach) on
macOS. Standalone spike code, not integrated into the product app yet.

**Acceptance Criteria:**
- A written finding per platform states: whether reliable, permission-
  appropriate system-audio capture is achievable without requiring the
  user to install a third-party virtual audio driver; what the actual
  integration cost looks like; and a clear recommendation on whether this
  ships as part of Phase 1.5 or is pushed further out.
- If a working PoC is achieved on either platform, it captures loopback
  audio in the same normalized-PCM shape the rest of the pipeline expects
  (per `PH1-CORE-002`).

**Out of Scope:** Android/iOS (per ADR-0011); integrating a positive
finding into the shipping product (that becomes its own follow-up task
once this spike concludes).

**Suggested Verification:** Findings note per platform, committed
alongside any PoC code.

**Completion note (2026-10-20):** Done. Built standalone Windows WASAPI loopback capture spike
in `spikes/PH1.5-REC-005/WasapiLoopbackCapture.cs` and CLI runner `spikes/PH1.5-REC-005/Program.cs`.
Verified zero-driver user-mode loopback capture on default render endpoint (`48000 Hz, 2 ch, Float`),
downmixing to mono, linear resampling to 16,000 Hz, and writing normalized 16-bit PCM with valid
44-byte RIFF/WAV header. Documented written findings and recommendations across Windows, macOS, and
mobile in `spikes/PH1.5-REC-005/FINDINGS.md`. Recommendation: Ship in Windows Desktop v1.5; macOS
via ScreenCaptureKit follow-up; strictly skip mobile per ADR-0011.
