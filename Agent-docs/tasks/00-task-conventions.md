# Task Conventions for Implementing Agents (Hermes or otherwise)

There is no dedicated "AI-agent task format" tool in use for this
project, so this document defines the contract explicitly. Every task in
every `phaseN-*.md` file in this folder follows the template below. An
implementing agent should treat any task not following this shape as
incomplete and flag it rather than guess.

## Task ID scheme

`PHx-AREA-NNN` — e.g. `PH1-CORE-004`. `PHx` matches the phase file it
lives in (`PH0`, `PH1`, `PH1.5`, `PH2`). `AREA` is a short area tag
(`CORE`, `STORE`, `EXPORT`, `UI`, `MODEL`, `REC`, `SPEAKER`, `INT`,
`SPIKE`). `NNN` is a zero-padded sequence number, unique within its
`PHx-AREA` pair.

## Required fields per task

- **Title** — one line, imperative ("Implement X", not "X").
- **Status** — `Not Started` / `In Progress` / `Blocked` / `Done`. The
  implementing agent updates this field directly in the task file as work
  progresses; do not track status anywhere else.
- **Depends On** — explicit task IDs that must be `Done` first. `None` if
  truly independent. An agent must not start a task whose dependencies
  are not `Done`, and must not silently reorder the dependency graph.
- **Related ADR(s)** — which `adr/000N-*.md` file(s) this task implements
  or must respect. Read them before starting.
- **Goal** — one to two sentences, plain language, no implementation
  detail.
- **Scope** — the explicit project(s)/file(s)/module(s) this task touches.
  If a task needs to modify something outside its stated scope, that is a
  signal the task is mis-sliced — stop and flag it rather than silently
  expanding scope.
- **Interface / Contract** (where relevant) — if this task defines an
  interface other tasks will consume, or consumes one another task
  defines, spell it out literally (a C# signature, a JSON shape, a file
  format). Do not leave a shared contract implicit.
- **Acceptance Criteria** — a bullet list, each item independently
  checkable (ideally by a specific test or a concrete manual repro step).
  A task is not `Done` until every item is checked.
- **Out of Scope** — explicit exclusions, to prevent scope creep into
  adjacent, separately-ticketed work.
- **Suggested Verification** — how to confirm this task actually works:
  a unit test, an integration test, a manual repro on a named device
  tier, or (for spikes) a findings note.

## Sizing rule

A task should be completable and independently verifiable on its own. If
describing a task in this template requires an "and also," it is too big
— split it into two tasks and add a `Depends On` link between them if
order matters.

## Spike vs. Build tasks

Tasks tagged `(spike)` in their title produce a **findings note**
(markdown, committed alongside the code it was validated with, if any) as
their deliverable — not production code intended to ship. A spike's
acceptance criteria are about answering a question, not shipping a
feature. Do not skip a spike's findings note even if the code "just
worked" — the note is what the next task's author reads before starting.

## When a task reveals an ADR should change

**Do not silently deviate from an ADR.** If implementing a task surfaces
evidence that an accepted ADR's decision is wrong or needs updating, stop
and write a new, numbered ADR that explicitly supersedes the old one
(never edit an ADR in place — see the root `README.md`). Reference the
task that surfaced the issue. Only proceed with implementation once the
new ADR is written, even if that means the current task is temporarily
`Blocked`.

## Cross-task interface discipline

Several tasks define an interface consumed by later tasks in a different
area (for example, `PH1-CORE-005` defines `IAsrBackend`, consumed by
`PH1-CORE-006`, `PH1.5-REC-002`, and others). When a task like this is
`Done`, its exact interface shape becomes load-bearing for every
dependent task — do not change its signature later without checking every
consumer, and flag any such change as its own task rather than a silent
edit inside unrelated work.
