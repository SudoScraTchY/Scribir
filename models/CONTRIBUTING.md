# Contributing to the Scribir Model Catalog

This guide explains how to propose, benchmark, and add new speech recognition (ASR), diarization, or Voice Activity Detection (VAD) models to the Scribir catalog.

---

## 1. Zero-Hosting Philosophy (ADR-0009)

Scribir is strictly **zero-hosting**:
- **We never host, mirror, or proxy model binaries** through Scribir-controlled servers or S3 buckets.
- All downloads are performed directly by the end-user's device from upstream original publishers (e.g., **Hugging Face** official model repositories, official GitHub release artifacts).
- Every file entry in `catalog.json` must provide an immutable direct download URL and a cryptographic **SHA-256** checksum.

---

## 2. Schema Requirements

All entries in `catalog.json` must validate against [`manifest.schema.json`](./manifest.schema.json).

### Required Fields for Each Model:
- **`id`**: Unique lowercase alphanumeric identifier with hyphens (e.g., `sherpa-whisper-small-int8`).
- **`name`**: Human-readable name (e.g., `"Whisper Small (INT8) Multilingual"`).
- **`model_type`**: One of `"asr"`, `"diarization"`, or `"vad"`.
- **`language_codes`**: List of ISO 639-1 language codes (e.g., `["en"]`, `["fa"]`, `["multilingual"]`).
- **`device_tier_recommendation`**: `"low"`, `"medium"`, or `"high"`.
  - `"low"`: Operates comfortably on devices with $\le 4\text{ GB}$ RAM (e.g., Silero VAD, mobile int8 models).
  - `"medium"`: Target for desktop or 6–8 GB RAM devices (e.g., Whisper small/base).
  - `"high"`: Requires high-end desktop or discrete GPU.
- **`size_bytes`**: Exact total download size in bytes across all files.
- **`quant_variant`**: Precision / quantization (`"int8"`, `"fp16"`, `"fp32"`, `"onnx-quantized"`).
- **`license`**: Upstream license (e.g., `"Apache-2.0"`, `"MIT"`).
- **`source_url`**: Canonical upstream model card on Hugging Face (`https://huggingface.co/...`) or GitHub.
- **`date_added`**: Date added in `YYYY-MM-DD` format.
- **`files`**: Array of file objects required for inference:
  - `filename`: Local target filename (e.g., `"encoder.int8.onnx"`).
  - `url`: Direct raw download URL.
  - `sha256`: Lowercase 64-character hex SHA-256 checksum.
  - `size_bytes`: Byte count of this file.
- **`measured_wer`**:
  > [!IMPORTANT]
  > **Strict Accuracy Rule:** `measured_wer` must be set to `null` unless you have performed a verified benchmark on an official public evaluation split (such as Common Voice or LibriSpeech). **Never insert estimated, marketing, or placeholder numbers.**
- **`benchmark_notes`**: Optional string describing test conditions (dataset, split, RTF, hardware).

---

## 3. Submitting a Pull Request

1. Add your entry to [`catalog.json`](./catalog.json).
2. Validate against [`manifest.schema.json`](./manifest.schema.json) by running the test suite:
   ```bash
   dotnet test src/Scriber.Storage.Tests
   ```
3. Open a Pull Request detailing:
   - Upstream link & author credit.
   - Intended use case / target language.
   - Hardware test results (RTF, memory usage, device tier).
