# Phase 1 — Model Manager Tasks

---

### PH1-MODEL-001 — Model manifest schema

**Status:** Not Started
**Depends On:** None
**Related ADR(s):** ADR-0009

**Goal:** Define the versioned schema every manifest entry (and every
future community contribution) must follow.

**Scope:** A schema definition (JSON Schema or equivalent) plus the
initial (possibly empty) manifest file, living under `models/` per the
repo structure in `product/01-product-specification.md`.

**Interface / Contract:** Each entry must include at minimum: `id`,
`languageCodes` (array), `deviceTierRecommendation`, `sizeBytes`,
`quantVariant`, `license`, `sourceUrl` (must point at the original host,
e.g. Hugging Face — never a project-controlled mirror, per ADR-0009),
`measuredWer` (nullable — `null` until real benchmark data exists, never
a placeholder number), `dateAdded`.

**Acceptance Criteria:**
- Schema is documented in a form a community contributor can follow
  without reading code (e.g., a short `models/CONTRIBUTING.md`).
- Schema validation rejects an entry missing a required field or pointing
  `sourceUrl` at anything other than a recognized original-source
  pattern.

**Out of Scope:** Populating the manifest with real entries (task
`PH1-MODEL-004`).

**Suggested Verification:** Schema validation unit tests with both valid
and deliberately invalid example entries.

---

### PH1-MODEL-002 — Model Manager UI

**Status:** Not Started
**Depends On:** PH1-MODEL-001, PH1-STORE-002
**Related ADR(s):** ADR-0009

**Goal:** Let users browse manifest entries by language and device tier,
see license/size before downloading, and manage (download/delete) local
models.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- Entries are browsable/filterable by language and device tier.
- License and size are shown clearly before a download starts — never
  after.
- Download progress and state (per `PH1-STORE-002`'s state machine) are
  visible.
- It is visually unambiguous when the app is about to reach the network
  (per the root `README.md`'s standing constraint that model download is
  the one explicit, clearly-separated exception to offline operation).

**Out of Scope:** The first-run benchmark UI itself (next task, though
this screen may surface its recommendation).

**Suggested Verification:** Manual test of browse/filter/download/delete
flows on both platforms.

---

### PH1-MODEL-003 — First-run device benchmark harness

**Status:** Not Started
**Depends On:** PH1-CORE-004, PH1-CORE-005
**Related ADR(s):** ADR-0006 (see also `PH0-SPIKE-004` findings)

**Goal:** Replace guesswork with measurement: at first run, time 2–3
candidate model configurations on the user's actual device and recommend
a default, per the differentiator described in
`product/02-motives-and-differentiators.md` §3.3.

**Scope:** `Scriber.App` + `Scriber.Pipeline`.

**Acceptance Criteria:**
- Runs a short (~5 second) fixed test clip through 2–3 candidate model
  configurations on first launch.
- Measures wall-clock time and approximate memory pressure for each.
- Completes within a user-tolerable time budget — **the exact number
  must be derived from `PH0-SPIKE-004`'s real device data, not guessed
  here; if that data isn't yet available when this task starts, it is
  Blocked on `PH0-SPIKE-004`.**
- Writes the resulting recommendation into local settings as the default
  model selection, and lets the user override it manually afterward.
- Result is never silently final — always reachable and re-runnable from
  Settings.

**Out of Scope:** Populating `product/03-model-device-matrix-template.md`
with aggregate findings across many users' devices (that table is filled
by the Phase 0 spikes' controlled testing, not by this in-app,
single-device benchmark).

**Suggested Verification:** Manual test on the same device tiers named in
`PH0-SPIKE-004`, confirming the recommendation is sensible relative to
that spike's findings.

---

### PH1-MODEL-004 — Populate initial manifest entries

**Status:** Not Started
**Depends On:** PH0-SPIKE-003, PH1-MODEL-001
**Related ADR(s):** ADR-0005, ADR-0009

**Goal:** Ship the manifest with real, vetted entries: the default
multilingual model and the curated Persian model identified by
`PH0-SPIKE-003`.

**Scope:** `models/` manifest file.

**Acceptance Criteria:**
- At least one general-purpose multilingual entry and one Persian-
  specific entry, each with a real license, real source URL, and (where
  `PH0-SPIKE-003` produced one) a real measured accuracy note.
- No entry contains a guessed or placeholder `measuredWer` — `null` is
  correct until real data exists, per `PH1-MODEL-001`'s contract.

**Out of Scope:** Community-contributed entries beyond these two
launch-critical ones.

**Suggested Verification:** Manifest schema validation (via
`PH1-MODEL-001`'s tests) plus a manual link-check that each `sourceUrl`
resolves to the correct, real model page.
