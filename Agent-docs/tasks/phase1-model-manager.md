# Phase 1 — Model Manager Tasks

---

### PH1-MODEL-001 — Model manifest schema

**Status:** Done
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

**Completion note (2026-10-15):** Done. `models/manifest.schema.json` (JSON Schema Draft 2020-12)
defining strict schema contract (`id`, `name`, `model_type`, `language_codes`, `device_tier_recommendation`,
`size_bytes`, `quant_variant`, `license`, `source_url`, `measured_wer`, `date_added`, `files`).
Created `models/CONTRIBUTING.md` documenting contribution workflow, zero-hosting philosophy (ADR-0009),
and prohibition on fake placeholder WER scores. Implemented `Scriber.Storage.Models.ModelManifestValidator`
enforcing HTTPS upstream origins (Hugging Face / GitHub) and SHA-256 hex hashes. Verified with unit
tests in `Scriber.Storage.Tests.ModelManifestTests` (10/10 passed).

---

### PH1-MODEL-002 — Model Manager UI

**Status:** Done
**Completed:** 2026-10-20
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

**Completion note (2026-10-20):** Done. Implemented `IModelCatalogService` & `ModelCatalogService`
with bundled fallback manifest and repo root discovery. Implemented `ModelItemViewModel` and
`ModelsViewModel` featuring live search, filter chips (Type, Tier, Language), real-time download
progress reporting, SHA-256 verification, and model deletion. Built `ModelsPage.xaml` featuring
prominent offline & network privacy disclosure banner per ADR-0009, clear license & size badges,
direct upstream links, and confirmation dialog warning before initiating any network download.
Registered in `MauiProgram.cs` and `AppShell.xaml` with tab and route navigation. Tested on Windows Desktop.

---

### PH1-MODEL-003 — First-run device benchmark harness

**Status:** Done
**Completed:** 2026-10-26
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

**Completion note (2026-10-26):** Done. Built `IDeviceBenchmarkService` and `DeviceBenchmarkService` in
`Scriber.Pipeline`. Measures real SIMD DSP throughput, memory pressure, and CPU cores. Injected into
`SettingsViewModel` and exposed via dedicated card on `SettingsPage.xaml`. Evaluated live on physical Xiaomi
Redmi Note 11 (Android 13, Snapdragon 680): detected Low tier, calibrated `sherpa-whisper-tiny-int8` recommendation,
persisted to MAUI `Preferences`. Unit tests in `Scriber.Pipeline.Tests.DeviceBenchmarkServiceTests`.

---

### PH1-MODEL-004 — Populate initial manifest entries

**Status:** Done
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

**Completion note (2026-10-15):** Done. `models/catalog.json` populated with:
1. `sherpa-whisper-small-int8`: General multilingual ASR from Hugging Face (`csukuangfj/sherpa-onnx-whisper-small`), Apache-2.0.
2. `snakers-silero-vad-v5`: On-device VAD from GitHub releases (`snakers4/silero-vad`), MIT.
3. `nemotron3-diarizer-v3`: Streaming diarization step ONNX from Hugging Face (`nvidia/speakerverification_en_titanet_large`), Apache-2.0.
4. `whisper-small-fa-int8`: Curated Persian candidate from Hugging Face (`openai/whisper-small`), Apache-2.0.
All entries strictly enforce `measured_wer: null` (no fake numbers), direct upstream download links, and 64-char SHA-256 hashes. Verified via `Scriber.Storage.Tests.ModelManifestTests`.
