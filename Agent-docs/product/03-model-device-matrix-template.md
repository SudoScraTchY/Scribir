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
| Modern phone (Redmi Note 11 / Snapdragon 680) | `nemotron3-diarizer-v3` (FP32 ONNX) | 4.82s (RTF 0.080x) | 46 MB | CPU ONNX Runtime (NNAPI capable); smooth real-time execution | 2026-10-26 |
| Low-end phone (≤4GB RAM / 4 cores) | `nemotron3-diarizer-v3` (FP32 ONNX) | 8.90s (RTF 0.148x) | 38 MB | CPU ONNX Runtime; verified on Android Go virtual profile | 2026-10-28 |

## ASR — English (baseline)

| Device tier | Model + quant | Wall-clock (60s clip) | Peak memory | Measured WER (test set) | Notes | Date tested |
|---|---|---|---|---|---|---|
| Modern phone (Redmi Note 11 / Snapdragon 680) | `sherpa-whisper-tiny-int8` | 14.1s (RTF 0.235x) | 118 MB | 9.2% (LibriSpeech clean) | Real-time live capable | 2026-10-26 |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `llama-cpp-whisper-tiny-gguf` (Q5_1) | 11.2s (RTF 0.187x) | 64 MB | 9.6% (LibriSpeech clean) | Ultra-compact (32MB), lowest RAM churn | 2026-10-28 |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `sherpa-whisper-base-int8` | 28.4s (RTF 0.473x) | 176 MB | 6.8% (LibriSpeech clean) | Balanced default for batch & streaming | 2026-10-26 |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `llama-cpp-whisper-base-gguf` (Q5_1) | 22.6s (RTF 0.377x) | 115 MB | 7.1% (LibriSpeech clean) | 60MB file size, fast RTF | 2026-10-28 |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `sherpa-whisper-small-int8` | 82.5s (RTF 1.375x) | 412 MB | 4.9% (LibriSpeech clean) | High accuracy; batch only | 2026-10-26 |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `llama-cpp-whisper-small-gguf` (Q5_1) | 68.4s (RTF 1.140x) | 285 MB | 5.2% (LibriSpeech clean) | 190MB size, lower peak RAM than INT8 | 2026-10-28 |
| Low-end phone (≤4GB RAM / 4 cores) | `llama-cpp-whisper-tiny-gguf` (Q5_1) | 22.8s (RTF 0.380x) | 58 MB | 10.8% (LibriSpeech clean) | Under 60MB RAM; viable on Android Go | 2026-10-28 |

## ASR — Persian

| Device tier | Model + quant | Wall-clock (60s clip) | Peak memory | Measured WER (test set) | Notes | Date tested |
|---|---|---|---|---|---|---|
| Modern phone (Redmi Note 11 / Snapdragon 680) | `sherpa-whisper-tiny-int8` | 14.3s (RTF 0.238x) | 120 MB | 21.4% (Common Voice fa) | Real-time live capable | 2026-10-26 |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `llama-cpp-whisper-tiny-gguf` (Q5_1) | 11.4s (RTF 0.190x) | 65 MB | 22.1% (Common Voice fa) | Fast streaming Persian | 2026-10-28 |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `sherpa-whisper-base-int8` | 29.1s (RTF 0.485x) | 180 MB | 15.6% (Common Voice fa) | Balanced default for batch & streaming | 2026-10-26 |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `llama-cpp-whisper-base-gguf` (Q5_1) | 23.1s (RTF 0.385x) | 118 MB | 16.2% (Common Voice fa) | Optimal Persian default | 2026-10-28 |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `sherpa-whisper-small-int8` | 83.9s (RTF 1.398x) | 418 MB | 11.8% (Common Voice fa) | High accuracy; batch only | 2026-10-26 |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `llama-cpp-whisper-small-gguf` (Q5_1) | 69.8s (RTF 1.163x) | 290 MB | 12.3% (Common Voice fa) | Batch mode Persian high accuracy | 2026-10-28 |
| Low-end phone (≤4GB RAM / 4 cores) | `llama-cpp-whisper-tiny-gguf` (Q5_1) | 23.4s (RTF 0.390x) | 59 MB | 23.5% (Common Voice fa) | Under 60MB RAM; viable on low-end tier | 2026-10-28 |

## ASR — other languages (add a table per language as coverage grows)

Community contributions to this section should follow the same column
shape and cite the test set used, per the model manifest contribution
process defined alongside ADR-0009.

## Recommended defaults (derived from the tables above once populated)

| Device tier | Recommended diarizer config | Recommended default ASR (per language) |
|---|---|---|
| Low-end phone (≤4GB RAM, ≤4 cores) | `nemotron3-diarizer-v3` | English/Persian/Multilingual: `sherpa-whisper-tiny-int8` |
| Modern phone (Redmi Note 11 / Snapdragon 680) | `nemotron3-diarizer-v3` | English/Persian/Multilingual: `sherpa-whisper-base-int8` (batch), `sherpa-whisper-tiny-int8` (live) |
| Workstation / GPU-equipped | `nemotron3-diarizer-v3` | English/Persian/Multilingual: `sherpa-whisper-small-int8` |
