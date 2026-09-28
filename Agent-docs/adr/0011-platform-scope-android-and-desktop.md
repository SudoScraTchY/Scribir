# ADR-0011: Platform Scope — Android and Desktop Required for v1; iOS Deferred

**Status:** Accepted

## Context

Explicit product requirement: the app must work on Android, and a
desktop version is also required — both, not one sequenced long after
the other. iOS was not named as a v1 requirement, and carries extra,
well-known friction: App Store review, and — critically for the system-
audio-capture ambitions in `product/02-motives-and-differentiators.md`
§3.6 — no general system-audio loopback available to third-party apps on
iOS outside specific, restrictive frameworks.

## Decision

**v1 MVP targets Android and Desktop (Windows/macOS) simultaneously.**
iOS is explicitly deferred to Phase 2.5+, to be revisited once Android
and desktop learnings (particularly around on-device inference
performance and any system-audio investigation) reduce its risk.

System-audio loopback capture (as opposed to microphone input and file
import) is scoped as its **own platform-by-platform investigation**
(`PH1.5-REC-005`, Windows/macOS only for now) and is **not assumed** to
ship as part of the core v1 MVP on any platform, including desktop.

## Consequences

- v1's feature set must be fully achievable using **microphone input and
  file import alone** on Android, without depending on any Android
  system-audio-loopback capability that may not be reliably available
  across OEMs and API levels.
- Mobile on-device inference performance (ADR-0006) becomes a first-class
  v1 concern, not a "nice to have later" one, since Android ships in the
  same release as desktop rather than following it.
- iOS's harder system-audio constraints are deferred rather than solved
  around — this avoids compromising the desktop/Android experience to
  accommodate iOS's more restrictive platform APIs prematurely.

## Alternatives considered

- **Desktop-first, Android in a later phase:** This was the original
  planning-draft sequencing; superseded by the explicit requirement that
  Android ship in v1 alongside desktop, not after it.
- **All three platforms (including iOS) in v1:** Rejected given iOS's
  additional constraints were not named as a v1 requirement and would
  meaningfully increase scope for marginal near-term benefit.
