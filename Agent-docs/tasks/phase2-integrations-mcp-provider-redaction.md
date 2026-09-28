# Phase 2 — Integrations: BYOK, MCP Server, Summarization, Redaction, Diff View

---

### PH2-INT-001 — BYOK provider configuration

**Status:** Not Started
**Depends On:** None directly
**Related ADR(s):** ADR-0007

**Goal:** Let a user configure any OpenAI-compatible endpoint — a
commercial API or a self-hosted/LAN-internal model — as an optional
provider for summarization or other opt-in AI-assisted features.

**Scope:** `Scriber.App` settings + a `Scriber.Providers` (new, small)
project defining the provider abstraction.

**Acceptance Criteria:**
- Settings accept a base URL, optional API key, and model name — no
  special-casing required for "self-hosted" vs. "commercial," per
  ADR-0007's decision that these are architecturally identical.
- A configured self-hosted/LAN endpoint works with zero requirement for
  general internet access beyond reaching that LAN address (verify with
  the device's internet connection disabled but the LAN endpoint
  reachable).
- No BYOK configuration is required for any core transcription feature to
  function — verify the app's core Phase 1 functionality is completely
  unaffected by BYOK being unconfigured.

**Out of Scope:** Any feature that actually calls the configured
provider (subsequent tasks).

**Suggested Verification:** Manual test with a real self-hosted
OpenAI-compatible endpoint on a local network, with general internet
access disabled.

---

### PH2-INT-002 — MCP server exposing transcripts and search

**Status:** Not Started
**Depends On:** PH1-STORE-001, PH1-EXPORT-002
**Related ADR(s):** ADR-0008

**Goal:** Expose transcripts, full-text search, and (once PH2-INT-003
exists) summaries as an MCP server — tools and resources any
MCP-compatible client (Hermes, OpenClaw, Claude Desktop, etc.) can use.

**Scope:** New project `Scriber.Mcp`.

**Interface / Contract:** At minimum, expose: a `list_transcripts` tool
(with basic filters — date range, language), a `search_transcripts` tool
wrapping `PH1-STORE-001`'s full-text search, and transcripts themselves as
readable resources in the structured-Markdown-with-frontmatter shape
defined by `PH1-EXPORT-002` (this format was chosen specifically because
it is both human- and machine-parseable).

**Acceptance Criteria:**
- A generic, off-the-shelf MCP client (an MCP inspector tool, or Claude
  Desktop configured to point at this server) can list, fetch, and search
  transcripts through this server with **no Scriber-specific client code**
  — this is the acceptance bar, since the whole point of ADR-0008 is
  protocol-level compatibility, not a bespoke integration.
- The server exposes no more than what's needed for these read/search
  operations — do not expose destructive operations (delete, overwrite)
  through MCP without a separate, explicit design/task for that, given
  the security posture concerns noted in ADR-0008's context.

**Out of Scope:** Any write/delete capability via MCP; framework-specific
Hermes or OpenClaw code (explicitly rejected in ADR-0008).

**Suggested Verification:** Manual test against a generic MCP client per
the acceptance criteria above.

---

### PH2-INT-003 — Optional local LLM summarization / action-item extraction

**Status:** Not Started
**Depends On:** PH2-INT-001, PH1-EXPORT-002
**Related ADR(s):** ADR-0007

**Goal:** Offer optional summarization/action-item extraction, routed
through the same local-or-BYOK provider abstraction from PH2-INT-001 —
never a separate, required path.

**Scope:** `Scriber.App` + `Scriber.Providers`.

**Acceptance Criteria:**
- Summarization is off by default and requires an explicit user action to
  run, using whichever provider (local model or BYOK) is configured.
- Core transcription and all Phase 1 functionality has zero dependency on
  this feature being enabled — verify by confirming the app is fully
  functional with no provider configured and this feature simply
  unavailable/hidden rather than erroring.
- If a local on-device summarization model is used, it follows the same
  quantization-by-default principle as ADR-0006, and is itself listed in
  the model manifest per ADR-0009 (no separate, undocumented bundling
  path for this model).

**Out of Scope:** Any first-party hosted summarization endpoint
(explicitly disallowed by ADR-0007).

**Suggested Verification:** Manual test with the feature both disabled
(confirm no impact on core app) and enabled via each provider mode
(local, BYOK).

---

### PH2-INT-004 — Local PII / redaction pass

**Status:** Not Started
**Depends On:** PH1-CORE-007
**Related ADR(s):** None directly (see
`product/02-motives-and-differentiators.md` §3.7 for rationale)

**Goal:** Flag or redact likely-sensitive content (names, emails, phone
numbers, and user-defined custom terms) before an export or share action
completes, entirely locally.

**Scope:** `Scriber.Pipeline/Redaction`, integrated into the export flow
(`Scriber.Export`).

**Acceptance Criteria:**
- Runs entirely on-device (a local NER model or a regex/dictionary-based
  approach is acceptable for v1 — no network call of any kind for this
  feature).
- Detected items are surfaced for review **before** export/share
  finalizes — the user can accept, reject, or manually adjust each
  flagged item individually.
- Supports user-defined custom terms (e.g., a client name, a case number)
  in addition to built-in detection categories.
- Never silently redacts without the review step described above.

**Out of Scope:** Redaction of audio itself (text-only for v1); this task
covers the export-time review pass, not permanent removal of the
original unredacted transcript from local storage.

**Suggested Verification:** Manual test with a fixture transcript
containing planted names/emails/phone numbers and a custom term, verifying
detection, review, and correct application to the exported output only.

---

### PH2-INT-005 — Diff-aware re-transcription

**Status:** Not Started
**Depends On:** PH1-STORE-001, PH1-CORE-006, PH1.5-REC-003 (its
edit-preservation groundwork)
**Related ADR(s):** None directly (see
`product/02-motives-and-differentiators.md` §3.4 for rationale)

**Goal:** When a transcript is re-processed (a model upgrade, or a manual
re-run), show what changed versus the previous version, and never
silently discard prior manual corrections.

**Scope:** `Scriber.Storage` (versioned transcript history) +
`Scriber.App` (diff view).

**Acceptance Criteria:**
- Re-running transcription on already-transcribed audio produces a new
  version rather than overwriting the prior one destructively.
- A diff view highlights word-level and speaker-attribution changes
  between the two versions, git-diff-style.
- Any manual edits/corrections made to the prior version are explicitly
  reconciled — surfaced to the user as "kept," "conflicting with new
  version," or "obsolete," never silently lost.
- The user can choose to keep the old version, adopt the new one, or
  selectively merge.

**Out of Scope:** Automatic three-way merge resolution — presenting the
conflict clearly to the user is sufficient for v1; automatic resolution
can be a later enhancement.

**Suggested Verification:** Manual test: transcribe, manually correct a
segment, re-run with a different model/config, confirm the correction is
surfaced as a reconciliation choice rather than silently overwritten.
