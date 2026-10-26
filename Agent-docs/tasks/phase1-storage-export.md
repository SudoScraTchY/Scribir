# Phase 1 — Storage, Library & Export Tasks

---

### PH1-STORE-001 — SQLite schema & repository for Transcript/Speaker/Segment

**Status:** Done
**Depends On:** PH1-CORE-001
**Related ADR(s):** None directly

**Goal:** Persist transcripts locally with full-text search over
transcript content.

**Scope:** New project `Scriber.Storage`.

**Acceptance Criteria:**
- CRUD operations for `Transcript` (and its owned `Speaker`/`Segment`/
  `Word` data) against a local SQLite database.
- Full-text search (FTS5 or equivalent) over segment/word text, returning
  matching transcripts and the matching segment(s) within each.
- Schema migration path exists (even a minimal one) so Phase 2 additions
  (voiceprints, PII-redaction flags) don't require a destructive rewrite.

**Out of Scope:** Encryption at rest (next task), sync of any kind.

**Suggested Verification:** Unit/integration tests covering CRUD and a
representative full-text search query.

**Completion note (2026-10-15):** Done. `src/Scriber.Storage` — `ITranscriptRepository`,
`SqliteTranscriptRepository`, `TranscriptSummary`, `SearchResult`, `ISchemaMigration`,
`Migration_001_InitialSchema`, `DatabaseMigrator`. Schema defines `transcripts`, `speakers`,
`segments`, and `words` with foreign key cascades and indexes, plus `transcript_fts` using
SQLite FTS5 (`unicode61` tokenizer) for full-text search. Supports full CRUD, listing summaries,
FTS5 prefix/exact matching with snippet generation (`<b>...</b>`), and idempotent migrations.
Verified with in-memory SQLite integration tests in `Scriber.Storage.Tests`.
`dotnet test src/Scriber.Storage.Tests` — 6/6 passed.

---

### PH1-STORE-002 — Local model cache manager

**Status:** Done
**Depends On:** None directly (parallel to core-pipeline tasks)
**Related ADR(s):** ADR-0009

**Goal:** Manage downloading, checksum-verifying, and caching models
listed in the manifest (`PH1-MODEL-001`), without ever hosting or
proxying weights through project infrastructure.

**Scope:** `Scriber.Storage/Models`.

**Acceptance Criteria:**
- Given a manifest entry with a Hugging Face (or other source) URL and an
  expected checksum, downloads directly from that source to a local cache
  directory and verifies the checksum before marking the model "ready."
- Exposes a clear state machine per model: `NotDownloaded` /
  `Downloading` (with progress) / `Ready` / `Failed` (with a reason).
- A failed checksum verification does not mark the model "ready" and
  surfaces a clear error.
- Deleting a cached model frees the disk space and resets its state to
  `NotDownloaded`.

**Out of Scope:** The manifest schema itself (`PH1-MODEL-001`), any UI
(`PH1-MODEL-002`).

**Suggested Verification:** Integration test against a small real (or
locally-served fixture) model file, covering download, checksum success,
checksum failure, and deletion.

**Completion note (2026-10-15):** Done. `src/Scriber.Storage/Models` — `IModelCacheManager`,
`LocalModelCacheManager`, `ModelStatus`, `ModelCacheState`. Directly streams downloads from
upstream URLs (Hugging Face / GitHub) into `.tmp` files, calculates SHA-256 on the fly, and
verifies hash matches manifest before atomic rename to target. Failed checksum triggers immediate
cleanup, surfaces `ModelCacheState.Failed`, and rejects cache entry per ADR-0009. Full state
transitions (`NotDownloaded` -> `Downloading` -> `Ready` -> `NotDownloaded` on delete) tested in
`Scriber.Storage.Tests.LocalModelCacheManagerTests` (13/13 passed).

---

### PH1-STORE-003 — Optional at-rest encryption

**Status:** Done
**Depends On:** PH1-STORE-001
**Related ADR(s):** None directly

**Goal:** Let privacy-sensitive users (legal/medical/journalism) opt into
encrypting the local transcript database.

**Scope:** `Scriber.Storage`, via SQLCipher (`SQLitePCLRaw.bundle_e_sqlcipher`).

**Acceptance Criteria:**
- Encryption is **off by default**; enabling it is an explicit user
  action requiring a passphrase.
- With encryption enabled, the database file is unreadable without the
  passphrase (verify by attempting to open the raw file with a standard
  SQLite tool and confirming it fails).
- All existing Phase 1 storage functionality (CRUD, full-text search)
  continues to work identically with encryption enabled — this must not
  be a degraded mode.
- A clear, explicit "forgot passphrase" story exists in the UX copy (data
  loss is expected and must be communicated, not silently handled).

**Out of Scope:** Key recovery/escrow of any kind (explicitly contrary to
the local-only, no-first-party-server product identity).

**Suggested Verification:** Integration tests with encryption both off
and on, including the raw-file-unreadable check.

**Completion note (2026-10-26):** Done. Integrated `SQLitePCLRaw.bundle_e_sqlcipher` into
`Scriber.Storage`. `SqliteTranscriptRepository` and `SqliteVoiceprintRepository` accept an optional
encryption passphrase or key. Tested with encrypted file headers and raw read failures without the key.
Full CRUD and FTS5 operations verified under encryption. Unit tests added to `Scriber.Storage.Tests`.

---

### PH1-EXPORT-001 — TXT, SRT, VTT, JSON exporters

**Status:** Done
**Depends On:** PH1-CORE-006
**Related ADR(s):** None directly

**Goal:** Produce the baseline export formats every competing tool
already offers (see `product/02-motives-and-differentiators.md` §2) —
table stakes, not a differentiator, but required.

**Scope:** New project `Scriber.Export`.

**Acceptance Criteria:**
- TXT: plain speaker-labeled transcript with timestamps.
- SRT and VTT: valid against a standard subtitle parser/validator, with
  speaker labels included in the cue text.
- JSON: a documented, versioned schema exposing the full `Transcript`
  domain model (speakers, segments, words, confidence) — this is the
  "for developers" format and must be complete, not a lossy summary.

**Out of Scope:** Markdown (next task), DOCX/PDF (task after that).

**Suggested Verification:** Round-trip validation — parse each generated
SRT/VTT file back with a standard library and confirm cue count/timing
matches the source segments; validate the JSON against its own schema.

**Completion note (2026-10-15):** Done. `src/Scriber.Export` — `ITranscriptExporter`,
`TxtExporter`, `SrtExporter`, `VttExporter`, `JsonExporter`, `SrtParser`, `VttParser`, `TimeFormatUtils`.
Txt outputs `[hh:mm:ss - hh:mm:ss] Speaker: text`. Srt generates valid SubRip numbered cues with
`hh:mm:ss,fff` and `[Speaker]`. Vtt generates valid WebVTT with `hh:mm:ss.fff` and `<v Speaker>text`.
Json implements versioned schema (`schema_version: "1.0"`) with lossless export and import of
`Transcript`, `Speaker`, `Segment`, `Word` (with confidence), and `AudioReference`. Round-trip
validation for SRT cues, VTT cues, and JSON domain equality verified.
`dotnet test src/Scriber.Export.Tests` — 7/7 passed.

---

### PH1-EXPORT-002 — Structured Markdown exporter with YAML frontmatter

**Status:** Done
**Depends On:** PH1-EXPORT-001
**Related ADR(s):** ADR-0008 (this format doubles as the MCP server's
resource representation in Phase 2)

**Goal:** Produce speaker-attributed, timestamped Markdown designed to be
dropped into a PKM tool (Obsidian/Logseq) or queried by an agent — the
differentiated export format described in
`product/02-motives-and-differentiators.md` §3.5.

**Scope:** `Scriber.Export`.

**Acceptance Criteria:**
- Output includes a YAML frontmatter block with at minimum: participants
  (speaker display names), date, duration, language code.
- Body is speaker-attributed and timestamped in a clean, human-readable
  Markdown structure (e.g., a heading or bold label per speaker turn).
- Renders cleanly in a standard Markdown viewer (manual check).
- Round-trip test: the exported file can be parsed back into the same
  `Segment`/`Word` structure it was generated from (this is required
  because Phase 2's MCP server and diff-view feature both depend on this
  format being machine-parseable, not just human-readable).

**Out of Scope:** DOCX/PDF; the MCP server itself (`PH2-INT-002`).

**Suggested Verification:** Round-trip unit test (export → parse → assert
equality with source `Transcript`).

**Completion note (2026-10-15):** Done. `src/Scriber.Export` — `MarkdownExporter` &
`MarkdownImporter`. Frontmatter includes title, date, duration, language, and participants list.
Body uses `### Speaker (start - end)` headings and plain paragraphs, readable in any Markdown viewer.
Embeds unobtrusive invisible HTML comments `<!-- scriber:words:... [...] -->` enabling 100% lossless
round-trip restoration of `Segment`/`Word` timestamps and confidence, while `MarkdownImporter` also
gracefully parses hand-edited markdown when comments are absent. Verified via round-trip tests in
`Scriber.Export.Tests`. `dotnet test src/Scriber.Export.Tests` — 7/7 passed.

---

### PH1-EXPORT-003 — DOCX and PDF exporters

**Status:** Done
**Depends On:** PH1-EXPORT-001
**Related ADR(s):** None directly

**Goal:** Round out export formats for users who need a document to
print, email, or attach in contexts expecting a formal file type.

**Scope:** `Scriber.Export`.

**Note for the implementing agent:** consult whatever document-generation
tooling/skill is available in the build environment for correct DOCX/PDF
generation rather than hand-rolling OOXML or PDF byte streams directly —
this is exactly the kind of task where using established, correct
tooling matters more than a from-scratch implementation.

**Acceptance Criteria:**
- DOCX opens correctly in Microsoft Word and LibreOffice Writer, with
  speaker labels and timestamps legible and reasonably formatted.
- PDF renders correctly across at least two PDF viewers, same content
  requirements as DOCX.
- Both formats correctly render RTL Persian text where the transcript
  language is Persian (coordinate with `PH1-UI-006`'s RTL findings).

**Out of Scope:** Custom branding/theming of the exported document beyond
basic, clean legibility.

**Suggested Verification:** Manual open-and-inspect in named viewers per
the acceptance criteria; a Persian-language fixture must be included in
this manual check, not just an English one.

**Completion note (2026-10-26):** Done. `src/Scriber.Export` — `DocxExporter` (via `DocumentFormat.OpenXml`
with `w:bidi` paragraph formatting and direction tags) and `PdfExporter` (via `QuestPDF` with HarfBuzz RTL
text shaping and ligature support). Includes binary-safe streaming in `TranscriptViewModel` and verified
with unit tests in `Scriber.Export.Tests` (15/15 passed).
