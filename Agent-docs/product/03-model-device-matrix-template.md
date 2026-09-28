# Model × Device-Tier Matrix — Template (unpopulated)

**Do not fill this in with guessed or remembered numbers.** Every cell in
the tables below must come from an actual measurement produced by a
Phase 0/1 task (`PH0-SPIKE-003`, `PH0-SPIKE-004`, and ongoing entries as
new models are evaluated). This file exists so that testing has a fixed
place to land, and so the in-app Model Manager and first-run device
benchmark (`tasks/phase1-model-manager.md`) have a real data source to
read from rather than hardcoded assumptions.

## Device tiers (definitions — fixed, do not change without a new ADR)

| Tier | Definition |
|---|---|
| Low-end phone | ≤4GB RAM, no dedicated NPU, 3+ year old SoC |
| Modern phone | NPU-capable (NNAPI/CoreML delegate available), current or near-current SoC |
| Budget laptop (CPU-only) | No dedicated GPU, integrated graphics only |
| Workstation / GPU-equipped | Discrete GPU available (CUDA/Vulkan/Metal) |

## Diarization model results

| Device tier | Model + quant | Wall-clock (60s clip) | Peak memory | Notes | Date tested |
|---|---|---|---|---|---|
| _(unfilled)_ | | | | | |

## ASR — English (baseline)

| Device tier | Model + quant | Wall-clock (60s clip) | Peak memory | Measured WER (test set) | Notes | Date tested |
|---|---|---|---|---|---|---|
| _(unfilled)_ | | | | | | |

## ASR — Persian

| Device tier | Model + quant | Wall-clock (60s clip) | Peak memory | Measured WER (test set) | Notes | Date tested |
|---|---|---|---|---|---|---|
| _(unfilled)_ | | | | | | |

## ASR — other languages (add a table per language as coverage grows)

Community contributions to this section should follow the same column
shape and cite the test set used, per the model manifest contribution
process defined alongside ADR-0009.

## Recommended defaults (derived from the tables above once populated)

| Device tier | Recommended diarizer config | Recommended default ASR (per language) |
|---|---|---|
| _(unfilled — populate only after real measurements exist)_ | | |
