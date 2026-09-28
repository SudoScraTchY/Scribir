# ADR-0008: Agent Ecosystem Integration via MCP Server

**Status:** Accepted

## Context

The product should be usable from within a user's own personal-agent
setup (Hermes Agent, OpenClaw, or similar) — e.g., "summarize my last
meeting" pulling from Scriber, or a finished transcript flowing into an
agent's memory/workspace automatically. Research clarified that Hermes
Agent and OpenClaw are personal-agent *runtimes* (messaging-gateway
daemons with MCP/ACP-based tool-use and their own memory systems), not
model providers — and that both have real, disclosed CVE histories,
arguing for a narrow, well-behaved integration rather than deep
framework-specific coupling.

## Decision

Expose Scriber's transcripts, search, and (optional) summaries as an
**MCP server** — tools and resources any MCP-compatible client can call —
rather than writing bespoke Hermes-specific or OpenClaw-specific
integration code.

## Consequences

- One implementation serves Hermes, OpenClaw, Claude Desktop, and any
  future MCP-compatible agent, rather than tracking two (or more) fast-
  moving external frameworks' own APIs individually.
- The integration surface is protocol-level and small, reducing exposure
  to upstream security issues in any one agent framework.
- The structured Markdown export format (`PH1-EXPORT-002`) is the natural
  resource representation the MCP server serves — these two tasks should
  share a data shape.

## Alternatives considered

- **Bespoke Hermes and/or OpenClaw plugins:** Rejected — doubles
  integration-maintenance surface for no capability MCP doesn't already
  provide, and ties Scriber's release cadence to two external, fast-
  moving, third-party projects.
- **No agent integration at all:** Rejected — this is an explicit, named
  product requirement, and MCP makes it cheap enough relative to the
  bespoke alternative that there's no reason to skip it.
