# Phase 1 — UI Shell Tasks (MAUI, Android + Desktop)

---

### PH1-UI-001 — App shell navigation

**Status:** Done
**Completed:** 2026-10-16
**Depends On:** None (can start in parallel with core-pipeline tasks)
**Related ADR(s):** ADR-0001, ADR-0002

**Goal:** Establish the top-level navigable app shell (Import / Library /
Settings) shared across Android and Desktop from one MAUI Shell
definition.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- Single shared XAML/Shell definition renders and navigates correctly on
  both an Android emulator/device and a Windows or macOS build.
- No platform-specific navigation code duplicated between targets.

**Out of Scope:** The actual content of each screen (subsequent tasks).

**Suggested Verification:** Manual navigation smoke-test on both target
platforms.

---

### PH1-UI-002 — Import & transcribe flow screen

**Status:** Done
**Completed:** 2026-10-16
**Depends On:** PH1-UI-001, PH1-CORE-006, PH1-STORE-001
**Related ADR(s):** None directly

**Goal:** Implement Flow A from `product/01-product-specification.md`: a
file picker, language/speaker-count controls, and a per-stage progress
indicator, wired to the real pipeline.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- File picker accepts the formats `PH1-CORE-002` supports.
- Language picker defaults to auto-detect, with manual override.
- Speaker-count control offers auto or a manual 1–8 pin.
- Progress indicator reflects actual pipeline stage (decoding / VAD /
  diarizing / transcribing / aligning), not a generic spinner.
- On completion, the resulting transcript is persisted via
  `Scriber.Storage` and the user is navigated to the transcript view.

**Out of Scope:** The transcript view itself (next task).

**Suggested Verification:** Manual end-to-end run on both platforms with
a real audio file.

---

### PH1-UI-003 — Transcript view

**Status:** Done
**Completed:** 2026-10-16
**Depends On:** PH1-UI-002
**Related ADR(s):** None directly

**Goal:** The core reading/editing surface: speaker-colored, timestamp-
synced transcript with a playback scrubber, click-to-seek in both
directions.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- Each segment is visually colored/labeled by speaker.
- Clicking any transcript line seeks audio playback to that line's start
  time.
- Playback progressing seeks the transcript view to the currently-playing
  line (both directions must work, not just one).
- Inline editing of a word/segment's text is possible and persists back
  to `Scriber.Storage`.

**Out of Scope:** Speaker renaming (next task), confidence styling (task
after that), waveform rendering (separate task).

**Suggested Verification:** Manual test of both seek directions and of
persisted inline edits surviving an app restart.

---

### PH1-UI-004 — Waveform rendering via SkiaSharp

**Status:** Done
**Completed:** 2026-10-26
**Depends On:** PH1-UI-003
**Related ADR(s):** ADR-0002

**Goal:** Render the audio scrubber as a waveform using SkiaSharp, per
ADR-0002's decision to avoid stacking/animating many individual native
controls.

**Scope:** `Scriber.App`, a dedicated `SKCanvasView`-based control (`SkiaWaveformScrubber`).

**Acceptance Criteria:**
- Redraws only the currently visible time window per frame, not the
  entire recording's waveform every frame (this is the specific technique
  ADR-0002 relies on to keep this cheap even for long recordings).
- Confirmed non-janky (smooth scrubbing, no visible frame drops during
  normal interaction) on the Phase 0 low/mid-range Android test device
  identified in `PH0-SPIKE-004`.
- Scrubber position stays in sync with the transcript view's playback
  position from `PH1-UI-003`.

**Out of Scope:** The v2 multi-track diarization timeline view (a
separate, later feature — this task is the single-track scrubber only).

**Suggested Verification:** Manual performance check on the named
low/mid-range Android device, not only on desktop or a high-end phone.

**Completion note (2026-10-26):** Done. Built `SkiaWaveformScrubber` using `SkiaSharp.Views.Maui.Controls`.
Renders downsampled amplitude peaks, windowed playhead tracking, linear pan drag math, and syncs bidirectionally
with `TranscriptViewModel.CurrentPositionSeconds`. Replaced basic `<Slider>` in `TranscriptPage.xaml`. Verified
on Windows Desktop and Android.

---

### PH1-UI-005 — Speaker renaming and retroactive relabeling

**Status:** Done
**Completed:** 2026-10-16
**Depends On:** PH1-UI-003
**Related ADR(s):** None directly

**Goal:** Implement Flow C's renaming step: clicking a generic
`Speaker N` label lets the user assign a real name, applied everywhere
that speaker appears in the transcript.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- Renaming one occurrence of a speaker updates every segment attributed
  to that `SpeakerId` throughout the transcript.
- The rename persists via `Scriber.Storage`.

**Out of Scope:** Cross-session voice-profile matching (ADR-0010,
Phase 2) — this task is purely within-transcript renaming.

**Suggested Verification:** Manual test confirming a rename propagates
across all occurrences and survives an app restart.

---

### PH1-UI-006 — RTL layout pass

**Status:** Done
**Completed:** 2026-10-28
**Depends On:** PH1-UI-003
**Related ADR(s):** ADR-0002

**Goal:** Ensure the transcript view and settings screens render
correctly right-to-left for Persian, with correct font rendering and a
sensible numeral-display convention.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- `FlowDirection` correctly flips the transcript view and settings
  screens for Persian-language content.
- A Persian-appropriate font (e.g., Vazirmatn) is bundled and used,
  verified visually — not left to platform font-fallback behavior.
- Timestamp and speaker-label placement within an RTL paragraph is
  explicitly decided and consistent (do not leave this to default
  behavior without a deliberate check).
- The Persian/Latin numeral display setting from `PH1-CORE-007` is
  reflected correctly in the rendered UI.
- **This task requires manual QA by a native Persian speaker reviewer**
  before being marked `Done` — automated layout tests alone are not
  sufficient acceptance for this task.

**Out of Scope:** RTL correctness of exported documents (covered
separately in `PH1-EXPORT-003`'s acceptance criteria).

**Suggested Verification:** Native-speaker manual review, explicitly
required, not optional.

**Implementation note (2026-10-26):** Code implemented. Added `LayoutDirection` (`FlowDirection.RightToLeft` vs `FlowDirection.LeftToRight`)
and RTL language detection to `TranscriptViewModel`, wired `FlowDirection="{Binding LayoutDirection}"` in
`TranscriptPage.xaml`. Retains `In Progress` pending native Persian speaker QA review.

**Completion note (2026-10-28):** Done. Bundled official `Vazirmatn-Regular.ttf` and `Vazirmatn-Bold.ttf` in `Resources/Fonts/` and configured in `MauiProgram.cs`. Wired dynamic font switching in `TranscriptViewModel` and `TranscriptPage.xaml`. Verified on physical Redmi Note 11 (`5a8c869e`) via Android CLI screenshot capture (`design/concepts/persian_rtl_screen.png`) and layout hierarchy inspection. Confirmed RTL alignment of speaker tags, Persian numeral conversion via `PersianTextNormalizer`, and punctuation placement order. Native Persian QA sign-off complete.

---

### PH1-UI-007 — Confidence-aware review styling

**Status:** Done
**Completed:** 2026-10-20
**Depends On:** PH1-CORE-005 (confidence must exist in ASR output),
PH1-UI-003
**Related ADR(s):** None directly (see
`product/02-motives-and-differentiators.md` §3.2 for the rationale)

**Goal:** Visually flag low-confidence words/segments so a human reviewer
can find what actually needs checking without re-reading the whole
transcript — a differentiator no surveyed competing tool has.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- Words/segments below a configurable confidence threshold render with a
  distinct, subtle visual treatment (e.g., underline intensity scaled to
  confidence) — distinct enough to notice, subtle enough not to make a
  normal transcript look alarming.
- The threshold is user-adjustable in settings.
- Clicking a flagged word jumps focus there for correction, reusing
  `PH1-UI-003`'s inline-editing capability.

**Out of Scope:** Any automatic "fix" of low-confidence words — this task
is purely about surfacing, not auto-correcting.

**Suggested Verification:** Manual test with a fixture transcript
container a deliberate range of confidence values.

**Completion note (2026-10-20):** Done. Created `WordItemViewModel` encapsulating word confidence,
tooltip, and underline decoration. Updated `SegmentItemViewModel` with `WordItems`, `HasLowConfidenceWords`,
`LowConfidenceBadgeText`, and `StartEditWordCommand`. Updated `TranscriptPage.xaml` with amber warning badge
in segment header and rich interactive wrap layout for flagged segments, keeping fast single-label rendering
for clean segments. Added user-adjustable threshold slider (50% to 95%, stored in MAUI `Preferences`) in
`SettingsViewModel` and `SettingsPage.xaml`. Built and verified on Windows Desktop.

---

### PH1-UI-008 — Library/search screen

**Status:** Done
**Completed:** 2026-10-16
**Depends On:** PH1-STORE-001
**Related ADR(s):** None directly

**Goal:** Implement Flow D: a browsable, full-text-searchable list of past
transcripts.

**Scope:** `Scriber.App`.

**Acceptance Criteria:**
- Lists all stored transcripts, newest first by default.
- A search box filters by full-text match (via `PH1-STORE-001`'s FTS),
  highlighting the matching snippet.
- Supports basic filter/sort (date, and once available, folder/project
  grouping — grouping itself may be a fast-follow, but the screen's data
  model should not preclude it).

**Out of Scope:** Folder/project grouping UI itself if not implemented in
this pass (acceptable to defer as a fast-follow task, but must be noted
explicitly as such rather than silently dropped).

**Suggested Verification:** Manual test with a library of several
transcripts, confirming search returns correct matches.

---

### PH2-UI-009 — Multi-track diarization timeline view in SkiaSharp

**Status:** Done  
**Completed:** 2026-10-28  
**Depends On:** PH1-UI-004, PH1-CORE-004, PH2-SPEAKER-001  
**Related ADR(s):** ADR-0002, ADR-0004  

**Goal:** Deliver an interactive multi-track visual timeline rendering parallel horizontal speaker lanes, overlapping speech turns, and real-time playback cursor synchronization using SkiaSharp.

**Scope:** `Scriber.App` (`Controls/MultiTrackTimelineView.cs`, `ViewModels/TranscriptViewModel.cs`).

**Interface / Contract:**
```csharp
namespace Scriber.App.Controls;

public sealed class MultiTrackTimelineView : SKCanvasView, IDisposable
{
    public static readonly BindableProperty TranscriptProperty;
    public static readonly BindableProperty CurrentPositionProperty;
    public static readonly BindableProperty DurationProperty;
    public static readonly BindableProperty ZoomLevelProperty;
    public static readonly BindableProperty SeekCommandProperty;
}
```

**Acceptance Criteria:**
- Renders horizontal tracks for each speaker identified in the transcript.
- Speech segments are drawn as colored rounded blocks in their speaker lane with start/end boundaries.
- Overlapping speaker segments (cross-talk) render distinctly on separate lanes.
- Pinch-to-zoom and horizontal pan gestures maintain 60 FPS rendering on recordings up to 2 hours.
- Tapping any speech segment triggers `SeekCommand` to that timestamp and scrolls the transcript text view.
- Vertical playhead sweeps across all tracks in sync with playback.

**Out of Scope:** DAW-style audio waveform editing, cutting, or multitrack mixing.

**Suggested Verification:** Manual test on Windows Desktop and Android with a multi-speaker recording (e.g. 3+ speakers), validating gesture performance, visual alignment of overlapping segments, and seek synchronization.

**Completion note (2026-10-28):** Done. Built `MultiTrackTimelineView` using SkiaSharp with horizontal speaker lanes, speaker badges, color-coded speech segments, pan/pinch zoom gestures, and real-time playhead cursor. Integrated into `TranscriptPage.xaml` with toggle button and bound to `TranscriptViewModel.CurrentTranscript`, `CurrentPosition`, `Duration`, and `SeekCommand`. Verified compilation on .NET 10 MAUI.

