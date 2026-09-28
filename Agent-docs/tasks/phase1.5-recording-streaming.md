# Phase 1.5 — Recording & Live Streaming Tasks

---

### PH1.5-REC-001 — Microphone capture

**Status:** Not Started
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

**Out of Scope:** System-audio loopback (separate task, PH1.5-REC-005).

**Suggested Verification:** Manual recording test on both platforms,
confirming permission prompts appear correctly and captured audio is
usable by the existing pipeline.

---

### PH1.5-REC-002 — Streaming diarization + streaming ASR wiring

**Status:** Not Started
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
- **If profiling during this task reveals GC-pause-driven audio-callback
  jitter that `Span<T>`/`ArrayPool<T>` cannot resolve, this is exactly
  the condition ADR-0012 pre-approved for introducing a narrowly-scoped
  Rust DSP crate — flag this explicitly rather than working around it
  with an unrelated hack.**

**Out of Scope:** The offline finalization pass (next task).

**Suggested Verification:** Manual live-recording test with deliberate
pause/resume, plus a profiling pass specifically checking for GC pauses
coinciding with audio-callback timing.

---

### PH1.5-REC-003 — Two-pass finalization

**Status:** Not Started
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

**Out of Scope:** The full diff-view UI (Phase 2, `PH2-INT-005`) — this
task only needs to preserve the information a future diff view would
need.

**Suggested Verification:** Manual test: record, make a live edit,
observe the finalization pass complete, confirm the edit was not lost.

---

### PH1.5-REC-004 — Pause/resume recording UI

**Status:** Not Started
**Depends On:** PH1.5-REC-001
**Related ADR(s):** None directly

**Goal:** Let the user pause and resume an in-progress recording cleanly.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- Pausing stops audio capture and streaming inference without corrupting
  state (verified jointly with `PH1.5-REC-002`'s streaming-state test).
- Resuming continues the same recording session (not a new one) with
  continuous timestamps.

**Out of Scope:** Multi-segment recordings treated as separate files
(pause/resume within one session only).

**Suggested Verification:** Manual pause/resume test with timestamp
continuity check.

---

### PH1.5-REC-005 — System-audio loopback capture investigation (spike, Windows/macOS only)

**Status:** Not Started
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
