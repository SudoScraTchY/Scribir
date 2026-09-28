# ADR-0009: Model Distribution — No First-Party Hosting; Redirect to Source; Community Manifest

**Status:** Accepted

## Context

Explicit product decision: the project will not host model weights or
sell/proxy API access of any kind, ever (see `product/01-product-
specification.md` §2).

## Decision

The in-app Model Manager **never bundles or proxies model weights**
through project infrastructure. Instead:

- A versioned **model manifest** (JSON/YAML — schema defined in
  `PH1-MODEL-001`) lists vetted models per language and device tier, each
  entry carrying: id, language(s), device-tier recommendation, size, quant
  variant, license, a direct link to its **original source** (principally
  Hugging Face), and measured accuracy once available (feeding
  `product/03-model-device-matrix-template.md`).
- Downloads happen **directly from the original source** — the app never
  sits in the middle as a mirror or proxy.
- The manifest format is documented and **open to community
  contribution** (pull requests adding or updating entries), which is the
  actual scaling mechanism for language coverage beyond what the
  maintainer alone could vet.

## Consequences

- Zero hosting cost or liability for the project — directly upholds the
  non-monetization, non-infrastructure identity.
- Download reliability and speed depend on third-party hosts (chiefly
  Hugging Face) rather than project-controlled infrastructure — an
  accepted trade-off given the constraint that created this decision.
- `THIRD_PARTY_LICENSES.md` at the repo root must catalogue every
  manifest-listed model's license, since licenses vary per model even
  when the app itself never redistributes the weights.

## Alternatives considered

- **Bundling one or two default models with the app install:** Rejected
  — even a "just the small default model" bundle is a step toward the
  project owning/distributing weights directly, which the explicit
  no-hosting decision rules out; a first-run download from the manifest,
  even for the default model, keeps the boundary clean.
