# ADR-0002: UI Styling — Native XAML + SkiaSharp, Blazor Hybrid Non-Default

**Status:** Accepted

## Context

Scriber needs a mostly "boring" productivity UI (transcript list,
settings, library/search) plus a small number of genuinely custom visual
elements (an audio waveform scrubber, per-speaker color/highlight
transitions, and possibly a v2 multi-track timeline). MAUI offers two
distinct styling paths: native XAML with MAUI's own CSS *styling* subset
(colors/fonts/margins — not a layout engine), and Blazor Hybrid, which
embeds a real HTML/CSS/JS webview via `BlazorWebView`.

## Decision

Build the primary app shell in **native XAML**, using MAUI's built-in CSS
subset for styling constants where convenient. Render the waveform and
any timeline/multi-track visuals through **SkiaSharp** (`SKCanvasView`) —
the same underlying rendering engine Flutter itself uses — rather than
stacking and animating many individual native shape controls. Reserve
`BlazorWebView`/Blazor Hybrid for a narrow, self-contained embedded
visualization only, if one is ever needed, not as the app's primary UI
technology.

## Alternatives considered

- **Blazor Hybrid as the primary UI:** Gets real CSS (flexbox/grid/
  transitions) at the cost of running inside a webview, reintroducing
  some of the same performance trade-offs discussed when Flutter was
  evaluated (ADR-0001). It also has a real, documented open issue: CSS
  Isolation (`.razor.css` scoped styles) failing iOS/macOS builds — a
  signal the Blazor-Hybrid-on-MAUI path is less traveled than plain XAML
  MAUI.
- **Stacking native shapes/controls for the waveform, individually
  animated:** This is the pattern that genuinely would be "heavy" in the
  earlier Flutter-vs-MAUI performance sense (many simultaneously animated
  native widgets). Rejected in favor of a single `SKCanvasView` that
  redraws a path, which scales cleanly even to a continuously-updating
  multi-track view as long as only the visible time window is redrawn
  per frame.

## Consequences

- No true CSS layout engine for the main app shell — layout stays XAML
  `Grid`/`StackLayout`, which is fine for the app's actual (non-animation-
  heavy) content profile.
- Native controls give correct RTL text shaping, text selection, and IME
  behavior "for free" from each platform's own text engine — directly
  relevant to Persian-language use (see `phase1-ui-shell.md`,
  `PH1-UI-006`).
- Waveform/speaker-highlight animation is **not** a heavy-animation risk
  for this UI profile as long as it is routed through SkiaSharp (or, for
  simple property animation like a color/opacity transition, MAUI's own
  native animation APIs) rather than reimplemented as many individually
  animated native shapes.
