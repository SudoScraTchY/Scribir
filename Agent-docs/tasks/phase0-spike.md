# Phase 0 — Spike Tasks

Goal of this phase: de-risk the diarizer/ASR/runtime pairing before any
product-layer (UI, storage, export) engineering begins. Nothing in
`phase1-*.md` should start until the relevant spike below is `Done` — see
each Phase 1 task's `Depends On`.

---

### PH0-SPIKE-001 — Validate sherpa-onnx against a custom Nemotron 3 Diarization export (spike)

**Status:** Not Started
**Depends On:** None
**Related ADR(s):** ADR-0003, ADR-0004

**Goal:** Determine whether sherpa-onnx's C# diarization API can
accommodate a custom ONNX export of Nemotron 3 Diarization, including its
streaming speaker-cache/FIFO parameters — or whether raw ONNX Runtime
integration is required for the diarizer specifically.

**Scope:** A standalone console app / scratch project only. No product
code.

**Acceptance Criteria:**
- Nemotron 3 Diarization is exported to ONNX following its own reference
  pipeline (consult the model's own `ASR_INTEGRATION_GUIDE.md`/streaming
  docs on its Hugging Face repo).
- A minimal console app runs the exported model through sherpa-onnx's C#
  diarization API surface on at least one test audio file, in both
  offline and streaming configurations.
- A written finding states clearly: (a) whether sherpa-onnx's existing
  API surface is sufficient as-is, (b) if not, exactly what is missing,
  and (c) a recommendation — proceed with sherpa-onnx, or fall back to
  raw `Microsoft.ML.OnnxRuntime` for the diarizer specifically.

**Out of Scope:** ASR integration, UI, storage.

**Suggested Verification:** Findings note committed to this repo (e.g.
`docs/findings/PH0-SPIKE-001.md`) plus the scratch console app.

---

### PH0-SPIKE-002 — End-to-end desktop proof of concept, one file, English

**Status:** Not Started
**Depends On:** PH0-SPIKE-001
**Related ADR(s):** ADR-0003, ADR-0004, ADR-0005

**Goal:** Prove the full pipeline shape (diarize → ASR → naive align)
works end to end on desktop before any UI exists.

**Scope:** Standalone console app.

**Acceptance Criteria:**
- Given a WAV file with 2–3 English speakers, the console app prints a
  speaker-labeled transcript (`[Speaker N] text...`) with timestamps.
- Uses whichever runtime path PH0-SPIKE-001 recommended for the diarizer,
  and a bundled small/quantized multilingual Whisper model for ASR.
- Output is subjectively plausible on manual review (correct speaker
  turns roughly matching a human's read of the same audio).

**Out of Scope:** Persian validation (next task), mobile, UI, storage,
export formats, confidence scoring.

**Suggested Verification:** Manual review of console output against the
source audio by a human reviewer.

---

### PH0-SPIKE-003 — Persian audio validation pass (spike)

**Status:** Not Started
**Depends On:** PH0-SPIKE-002
**Related ADR(s):** ADR-0004, ADR-0005

**Goal:** Empirically assess diarization turn-accuracy and ASR word-
accuracy on real Persian multi-speaker audio — do not assume parity with
published English/Mandarin numbers.

**Scope:** Extends the PH0-SPIKE-002 console app to accept additional
test files and, separately, to swap in each ASR candidate identified in
ADR-0005 (a Whisper Persian fine-tune, wav2vec2-XLSR-Persian, and a
community FastConformer Persian streaming model if available).

**Acceptance Criteria:**
- At least 3–5 real Persian multi-speaker recordings are run through the
  pipeline.
- Diarization turns are manually compared against ground truth and rated
  (accurate / partially accurate / poor) per recording.
- Each ASR candidate's output is manually assessed for word accuracy on
  the same recordings.
- Results (including which model is recommended as the Persian default)
  are written into `product/03-model-device-matrix-template.md`'s Persian
  ASR table, with real measured values only — no placeholder numbers left
  in the committed version of that table for the rows this task fills.

**Out of Scope:** Formal WER computation pipeline (manual assessment is
acceptable for this spike); mobile performance (next task).

**Suggested Verification:** Findings note + the populated matrix rows.

---

### PH0-SPIKE-004 — On-device mobile feasibility benchmark, Android (spike)

**Status:** Not Started
**Depends On:** PH0-SPIKE-002
**Related ADR(s):** ADR-0006, ADR-0011

**Goal:** Measure real wall-clock performance and memory pressure for the
quantized diarizer + a quantized small ASR model on actual Android
hardware, across the device-tier definitions in
`product/03-model-device-matrix-template.md`.

**Scope:** A minimal, instrumented Android test harness (CLI-style or a
throwaway single-screen test app is acceptable — this is not the product
UI).

**Acceptance Criteria:**
- Tested on at least one "modern phone" tier device (NPU-capable) and one
  "low-end phone" tier device (3+ year old, no NPU), per the tier
  definitions in the matrix template.
- INT8-quantized diarizer and ASR models are used, per ADR-0006.
- NNAPI execution provider is exercised where available; CPU-only
  fallback is also measured on both devices.
- Wall-clock time and peak memory for a fixed 60-second test clip are
  recorded for each device × configuration combination.
- Results are written into the diarization and ASR tables in
  `product/03-model-device-matrix-template.md`.
- A written recommendation states whether ADR-0006's "feasibility is
  fine, latency/battery is the real risk" assumption held up, or whether
  ADR-0006 needs a superseding ADR.

**Out of Scope:** iOS (deferred per ADR-0011), streaming-specific latency
measurement (covered later by `PH1.5-REC-002`'s own acceptance criteria
once live streaming exists).

**Suggested Verification:** Findings note + populated matrix rows +
explicit go/no-go statement on ADR-0006.
