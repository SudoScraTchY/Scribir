# ADR-0007: AI Provider Architecture — Local + BYOK Only

**Status:** Accepted

## Context

The product's identity is "free forever, private by construction, never
monetized" (see `product/02-motives-and-differentiators.md`). Any
provider architecture must not compromise that, while still giving users
a path to higher accuracy or relief from a low-end device if they
explicitly want it.

## Decision

Exactly **two** inference provider modes exist, and no more:

1. **Local, on-device** — via the runtime in ADR-0003, fully offline,
   the default and the floor every feature must work on top of.
2. **BYOK (bring-your-own-key)** — any **OpenAI-compatible endpoint** the
   user supplies: a commercial API (OpenAI, Groq, Deepgram, etc.) *or* a
   self-hosted/LAN-internal endpoint the user runs on their own network.
   The app treats both identically — just a base URL, an optional key,
   and a model name. This is a ceiling-raiser the user opts into and pays
   for (if anything) themselves; it is never how the app itself makes
   money, because the app makes no money.

There is **no third mode**. The project never runs, hosts, or resells
access to any inference endpoint of its own.

## Consequences

- Local model tier is the permanent floor; BYOK never becomes a
  requirement for any core feature.
- A self-hosted model on the user's own network requires no special
  casing — it is just another OpenAI-compatible base URL (`PH2-INT-001`).
- Optional local-LLM summarization (`PH2-INT-003`) and any future agent-
  facing feature route through this same two-mode abstraction rather than
  inventing a third path.

## Alternatives considered

- **A first-party hosted inference tier (even a "free" one):** Rejected
  outright — it would require project-funded server costs, directly
  contradicting the non-monetization identity and creating exactly the
  "trust us with your audio" problem the whole product exists to avoid.
- **Treating Hermes/OpenClaw as inference providers:** Rejected — research
  showed these are personal-agent *runtimes* (messaging-gateway, tool-use,
  memory), not model providers. Any of their configured LLMs are already
  reachable as a BYOK-style OpenAI-compatible endpoint if a user wants
  that; the actual integration surface for these tools is MCP (ADR-0008),
  a separate concern from inference provisioning.
