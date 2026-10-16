# Phase 2 — Cross-Session Speaker Identity Tasks

All tasks in this file are gated by ADR-0010 and are explicitly optional,
Phase 2 work — core per-session diarization (Phase 1) must remain fully
functional with none of this ever having run.

---

### PH2-SPEAKER-001 — Speaker embedding extraction

**Status:** Done
**Completed:** 2026-10-16
**Depends On:** PH1-CORE-004
**Related ADR(s):** ADR-0010, ADR-0003

**Goal:** Extract a speaker embedding (ECAPA-TDNN/Titanet-class model,
via sherpa-onnx per ADR-0003) for a given audio segment.

**Scope:** New capability in `Scriber.Inference.Diarization` or a sibling
project (`Scriber.Inference.SpeakerId`) — implementer's choice, but keep
it a distinct module from the core diarizer, since it is a genuinely
separate model.

**Interface / Contract:**
```csharp
public interface ISpeakerEmbedder
{
    Task<float[]> ExtractEmbeddingAsync(AudioBuffer segment, CancellationToken ct);
}
```

**Acceptance Criteria:**
- Given a clean 3–5 second speech segment, returns a fixed-length
  embedding vector.
- Embeddings for two segments from the same real speaker are
  measurably closer (by cosine similarity or equivalent) than embeddings
  from two different speakers, verified on a fixture set of known
  same/different-speaker pairs.

**Out of Scope:** Storage of embeddings (next task), matching logic
(task after that).

**Suggested Verification:** Unit test asserting the same/different-
speaker similarity property on fixture audio.

---

### PH2-SPEAKER-002 — Enrollment flow

**Status:** Done
**Completed:** 2026-10-16
**Depends On:** PH2-SPEAKER-001, PH1-STORE-001
**Related ADR(s):** ADR-0010

**Goal:** Let a user name a speaker and store their voice embedding
locally, either via a one-time calibration sentence or by harvesting a
clean segment from their first turn in a session.

**Scope:** `Scriber.App` + `Scriber.Storage` (new table for named local
voiceprints, covered by the same at-rest-encryption option as the rest of
the transcript library per ADR-0010's consequences).

**Acceptance Criteria:**
- User can name a speaker and trigger enrollment either from a dedicated
  "read this sentence" flow or by selecting an existing clean segment
  from a past transcript.
- The resulting embedding is stored locally, associated with the given
  name, and never transmitted anywhere (verify no network call occurs
  during enrollment).
- Voiceprint storage respects `PH1-STORE-003`'s encryption-at-rest toggle
  if enabled.

**Out of Scope:** Cross-session matching itself (next task).

**Suggested Verification:** Manual enrollment test + network-traffic
check confirming no data leaves the device during enrollment.

---

### PH2-SPEAKER-003 — Cross-session matching

**Status:** Done
**Completed:** 2026-10-16
**Depends On:** PH2-SPEAKER-002
**Related ADR(s):** ADR-0010

**Goal:** On a new recording, compare diarized speaker segments against
the stored voiceprint library and suggest names — never silently.

**Scope:** `Scriber.Pipeline`, integrating with `PH1-CORE-004`'s
diarization output.

**Acceptance Criteria:**
- Each diarized speaker in a new session is compared against all stored
  voiceprints; above a defined confidence threshold, a name suggestion is
  attached to that speaker.
- Every suggestion carries a visible confidence indicator in the UI (see
  `PH1-UI-005` for where renaming lives) and a one-tap way to accept,
  reject, or reassign it.
- **No suggestion is ever applied automatically without this visible
  accept/reject step** — this is a hard requirement from ADR-0010, not a
  UX nicety.
- Below the confidence threshold, the speaker falls back to a normal
  generic `Speaker N` label exactly as in Phase 1, with no incorrect
  suggestion forced on the user.

**Out of Scope:** Automatic silent identity assignment (explicitly
disallowed, see above).

**Suggested Verification:** Manual test across two separate recording
sessions featuring at least one previously-enrolled speaker, confirming
correct suggestion, correct confidence display, and correct behavior for
an unenrolled speaker in the same session.

---

### PH2-SPEAKER-004 — Voiceprint library management UI

**Status:** Done
**Completed:** 2026-10-16
**Depends On:** PH2-SPEAKER-003
**Related ADR(s):** ADR-0010

**Goal:** Let users manage their enrolled voiceprints directly — rename,
delete, or merge two profiles that were mistakenly enrolled separately
for the same real person.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- A dedicated settings screen lists all enrolled voiceprints by name.
- Rename and delete work as expected, with delete requiring confirmation
  (this removes biometric-adjacent data permanently).
- Merging two profiles combines their embeddings/history under one name
  without breaking past transcripts' existing speaker attributions.

**Out of Scope:** Bulk import/export of voiceprint libraries between
devices (not currently a stated requirement).

**Suggested Verification:** Manual test of rename, delete (with
confirmation), and merge.
