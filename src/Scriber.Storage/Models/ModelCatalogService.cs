using System.Text.Json;

namespace Scriber.Storage.Models;

/// <summary>
/// Default model catalog service loading and validating the model manifest.
/// Supports file-based loading with bundled JSON fallback.
/// Implements PH1-MODEL-001 / PH1-MODEL-002.
/// </summary>
public sealed class ModelCatalogService : IModelCatalogService
{
    private readonly string? _customCatalogPath;
    private ModelManifest? _cachedManifest;

    public ModelCatalogService(string? customCatalogPath = null)
    {
        _customCatalogPath = customCatalogPath;
    }

    public ModelManifest GetCatalog()
    {
        if (_cachedManifest != null)
        {
            return _cachedManifest;
        }

        string? json = null;

        // 1. Try custom path if provided
        if (!string.IsNullOrWhiteSpace(_customCatalogPath) && File.Exists(_customCatalogPath))
        {
            json = File.ReadAllText(_customCatalogPath);
        }

        // 2. Try AppContext / Repo root models/catalog.json
        if (json == null)
        {
            try
            {
                string candidatePath = Path.Combine(AppContext.BaseDirectory, "models", "catalog.json");
                if (File.Exists(candidatePath))
                {
                    json = File.ReadAllText(candidatePath);
                }
                else
                {
                    // Try walking up to find repo root with models/catalog.json
                    var dir = new DirectoryInfo(AppContext.BaseDirectory);
                    while (dir != null && dir.Parent != null)
                    {
                        string p = Path.Combine(dir.FullName, "models", "catalog.json");
                        if (File.Exists(p))
                        {
                            json = File.ReadAllText(p);
                            break;
                        }
                        dir = dir.Parent;
                    }
                }
            }
            catch
            {
                // Ignore sandboxed filesystem probe errors on mobile (Android/iOS)
            }
        }

        // 3. Fallback to bundled fallback catalog
        json ??= GetBundledCatalogJson();

        var manifest = JsonSerializer.Deserialize<ModelManifest>(json)
            ?? throw new InvalidOperationException("Failed to deserialize model catalog manifest.");

        ModelManifestValidator.Validate(manifest);
        _cachedManifest = manifest;
        return manifest;
    }

    private static string GetBundledCatalogJson()
    {
        return """
        {
          "schema_version": "1.0",
          "catalog_updated_at": "2026-10-23T20:00:00Z",
          "models": [
            {
              "id": "sherpa-whisper-small-int8",
              "name": "Whisper Small (INT8) Multilingual",
              "description": "General-purpose multilingual ASR model quantized to INT8 with attention-based token timestamps.",
              "model_type": "asr",
              "language_codes": ["multilingual", "en", "fa"],
              "device_tier_recommendation": "medium",
              "size_bytes": 375485327,
              "quant_variant": "int8",
              "license": "Apache-2.0",
              "source_url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small",
              "measured_wer": null,
              "benchmark_notes": "Sherpa-onnx export with encoder/decoder INT8 quantization.",
              "date_added": "2026-10-15",
              "files": [
                {
                  "filename": "small-encoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small/resolve/main/small-encoder.int8.onnx",
                  "sha256": "4cbe7b22fa9026b843b60a68640c747de05bafb1a11b57edc0e66c232d9f33a9",
                  "size_bytes": 112442483
                },
                {
                  "filename": "small-decoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small/resolve/main/small-decoder.int8.onnx",
                  "sha256": "acad50b5c782696e91b55914cc5ab4f756f1532f76e22aa6fc615f39fb69a8ee",
                  "size_bytes": 262226114
                },
                {
                  "filename": "small-tokens.txt",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small/resolve/main/small-tokens.txt",
                  "sha256": "b34b360dbb493e781e479794586d661700670d65564001f23024971d1f2fa126",
                  "size_bytes": 816730
                }
              ]
            },
            {
              "id": "sherpa-whisper-tiny-int8",
              "name": "Whisper Tiny (INT8) Multilingual",
              "description": "Ultra-compact multilingual Whisper model optimized for low-end mobile devices and fast real-time transcription.",
              "model_type": "asr",
              "language_codes": ["multilingual", "en", "fa"],
              "device_tier_recommendation": "low",
              "size_bytes": 103609903,
              "quant_variant": "int8",
              "license": "Apache-2.0",
              "source_url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-tiny",
              "measured_wer": null,
              "benchmark_notes": "Sherpa-onnx export of Whisper Tiny with INT8 quantization for edge mobile devices.",
              "date_added": "2026-10-23",
              "files": [
                {
                  "filename": "tiny-encoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-tiny/resolve/main/tiny-encoder.int8.onnx",
                  "sha256": "d24fb083ae3b1041fc24e97971d60e280c9342201fbb67b0ab428a8b4a51a434",
                  "size_bytes": 12937772
                },
                {
                  "filename": "tiny-decoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-tiny/resolve/main/tiny-decoder.int8.onnx",
                  "sha256": "d2fece8dd42771f1df975c6c0445770d0c292bf7547c2cae04a6c0cc57540925",
                  "size_bytes": 89855401
                },
                {
                  "filename": "tiny-tokens.txt",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-tiny/resolve/main/tiny-tokens.txt",
                  "sha256": "b34b360dbb493e781e479794586d661700670d65564001f23024971d1f2fa126",
                  "size_bytes": 816730
                }
              ]
            },
            {
              "id": "sherpa-whisper-base-int8",
              "name": "Whisper Base (INT8) Multilingual",
              "description": "Balanced multilingual Whisper model offering high speed and balanced accuracy for mobile and desktop.",
              "model_type": "asr",
              "language_codes": ["multilingual", "en", "fa"],
              "device_tier_recommendation": "low",
              "size_bytes": 160609290,
              "quant_variant": "int8",
              "license": "Apache-2.0",
              "source_url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-base",
              "measured_wer": null,
              "benchmark_notes": "Sherpa-onnx export of Whisper Base with INT8 quantization.",
              "date_added": "2026-10-23",
              "files": [
                {
                  "filename": "base-encoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-base/resolve/main/base-encoder.int8.onnx",
                  "sha256": "0b8fb1304b6109976038efff5ace81720e00386f3ff6b54ee8c75291ca0a1e11",
                  "size_bytes": 29120534
                },
                {
                  "filename": "base-decoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-base/resolve/main/base-decoder.int8.onnx",
                  "sha256": "9759d217388a01b3a4c7c15533201067b48ae819c4daafc8624e64b9409dc02d",
                  "size_bytes": 130672026
                },
                {
                  "filename": "base-tokens.txt",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-base/resolve/main/base-tokens.txt",
                  "sha256": "b34b360dbb493e781e479794586d661700670d65564001f23024971d1f2fa126",
                  "size_bytes": 816730
                }
              ]
            },
            {
              "id": "snakers-silero-vad-v5",
              "name": "Silero VAD v5",
              "description": "Ultra-lightweight on-device voice activity detector for streaming and pre-pass silence filtering.",
              "model_type": "vad",
              "language_codes": ["multilingual"],
              "device_tier_recommendation": "low",
              "size_bytes": 2327524,
              "quant_variant": "fp32",
              "license": "MIT",
              "source_url": "https://github.com/snakers4/silero-vad",
              "measured_wer": null,
              "benchmark_notes": "16 kHz pre-pass VAD model operating on 512-sample frames.",
              "date_added": "2026-10-15",
              "files": [
                {
                  "filename": "silero_vad.onnx",
                  "url": "https://github.com/snakers4/silero-vad/raw/master/src/silero_vad/data/silero_vad.onnx",
                  "sha256": "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3",
                  "size_bytes": 2327524
                }
              ]
            },
            {
              "id": "nemotron3-diarizer-v3",
              "name": "Nemotron-3 Streaming Diarizer",
              "description": "On-device speaker diarization neural network running via raw ONNX Runtime.",
              "model_type": "diarization",
              "language_codes": ["multilingual"],
              "device_tier_recommendation": "medium",
              "size_bytes": 4450144,
              "quant_variant": "fp32",
              "license": "Apache-2.0",
              "source_url": "https://huggingface.co/nvidia/speakerverification_en_titanet_large",
              "measured_wer": null,
              "benchmark_notes": "Streaming ONNX step model with Mel frontend and host speaker cache.",
              "date_added": "2026-10-15",
              "files": [
                {
                  "filename": "nemotron3_stream_step.onnx",
                  "url": "https://huggingface.co/nvidia/speakerverification_en_titanet_large/resolve/main/speakerverification_en_titanet_large.nemo",
                  "sha256": "32be35e06612f204d2a96f396f59e2c0500f590da5123c53faf6f1a0f0256575",
                  "size_bytes": 4450144
                }
              ]
            },
            {
              "id": "whisper-small-fa-int8",
              "name": "Whisper Small Persian Fine-Tuned (INT8)",
              "description": "Multilingual model evaluated for Persian speech transcription. Benchmark on hold per PH0-SPIKE-003.",
              "model_type": "asr",
              "language_codes": ["fa"],
              "device_tier_recommendation": "medium",
              "size_bytes": 375485327,
              "quant_variant": "int8",
              "license": "Apache-2.0",
              "source_url": "https://huggingface.co/openai/whisper-small",
              "measured_wer": null,
              "benchmark_notes": "Awaiting formal benchmark validation per PH0-SPIKE-003 directives.",
              "date_added": "2026-10-15",
              "files": [
                {
                  "filename": "small-encoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small/resolve/main/small-encoder.int8.onnx",
                  "sha256": "4cbe7b22fa9026b843b60a68640c747de05bafb1a11b57edc0e66c232d9f33a9",
                  "size_bytes": 112442483
                },
                {
                  "filename": "small-decoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small/resolve/main/small-decoder.int8.onnx",
                  "sha256": "acad50b5c782696e91b55914cc5ab4f756f1532f76e22aa6fc615f39fb69a8ee",
                  "size_bytes": 262226114
                },
                {
                  "filename": "small-tokens.txt",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-small/resolve/main/small-tokens.txt",
                  "sha256": "b34b360dbb493e781e479794586d661700670d65564001f23024971d1f2fa126",
                  "size_bytes": 816730
                }
              ]
            },
            {
              "id": "3dspeaker-eres2net-base",
              "name": "3D-Speaker ERes2Net Voiceprint Embedder",
              "description": "Extracts high-dimensional voiceprint embeddings for speaker verification and cross-session recognition.",
              "model_type": "diarization",
              "language_codes": ["multilingual"],
              "device_tier_recommendation": "medium",
              "size_bytes": 39593761,
              "quant_variant": "fp32",
              "license": "Apache-2.0",
              "source_url": "https://github.com/k2-fsa/sherpa-onnx",
              "measured_wer": null,
              "benchmark_notes": "ERes2Net voiceprint model extracted for 16 kHz audio with cosine similarity matching.",
              "date_added": "2026-10-23",
              "files": [
                {
                  "filename": "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
                  "url": "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
                  "sha256": "1a331345f04805badbb495c775a6ddffcdd1a732567d5ec8b3d5749e3c7a5e4b",
                  "size_bytes": 39593761
                }
              ]
            },
            {
              "id": "llama-cpp-whisper-base-gguf",
              "name": "Whisper Base (Q5_1 GGUF) - llama.cpp / whisper.cpp",
              "description": "GGUF quantized Whisper model running via llama.cpp / whisper.cpp backend on Android and Desktop.",
              "model_type": "asr",
              "language_codes": ["multilingual", "en", "fa"],
              "device_tier_recommendation": "low",
              "size_bytes": 59707625,
              "quant_variant": "q5_1",
              "license": "MIT",
              "source_url": "https://huggingface.co/ggerganov/whisper.cpp",
              "measured_wer": null,
              "benchmark_notes": "GGUF Q5_1 quantized model for whisper.cpp / llama.cpp high-efficiency inference.",
              "date_added": "2026-10-23",
              "files": [
                {
                  "filename": "ggml-base-q5_1.bin",
                  "url": "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin",
                  "sha256": "422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898",
                  "size_bytes": 59707625
                }
              ]
            },
            {
              "id": "llama-cpp-whisper-small-gguf",
              "name": "Whisper Small (Q5_1 GGUF) - llama.cpp / whisper.cpp",
              "description": "GGUF quantized Whisper Small model for higher accuracy running via llama.cpp / whisper.cpp.",
              "model_type": "asr",
              "language_codes": ["multilingual", "en", "fa"],
              "device_tier_recommendation": "medium",
              "size_bytes": 190085487,
              "quant_variant": "q5_1",
              "license": "MIT",
              "source_url": "https://huggingface.co/ggerganov/whisper.cpp",
              "measured_wer": null,
              "benchmark_notes": "GGUF Q5_1 quantized Small model for whisper.cpp / llama.cpp high-efficiency inference.",
              "date_added": "2026-10-23",
              "files": [
                {
                  "filename": "ggml-small-q5_1.bin",
                  "url": "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin",
                  "sha256": "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb",
                  "size_bytes": 190085487
                }
              ]
            },
            {
              "id": "llama-cpp-whisper-tiny-gguf",
              "name": "Whisper Tiny (Q5_1 GGUF) - llama.cpp / whisper.cpp",
              "description": "Ultra-compact Q5_1 quantized Whisper Tiny model (~32 MB). Lowest memory footprint and fastest inference speed for edge devices.",
              "model_type": "asr",
              "language_codes": ["multilingual", "en", "fa"],
              "device_tier_recommendation": "low",
              "size_bytes": 32152673,
              "quant_variant": "q5_1",
              "license": "MIT",
              "source_url": "https://huggingface.co/ggerganov/whisper.cpp",
              "measured_wer": null,
              "benchmark_notes": "GGUF Q5_1 quantized Tiny model for whisper.cpp / llama.cpp high-efficiency inference.",
              "date_added": "2026-10-27",
              "files": [
                {
                  "filename": "ggml-tiny-q5_1.bin",
                  "url": "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny-q5_1.bin",
                  "sha256": "818710568da3ca15689e31a743197b520007872ff9576237bda97bd1b469c3d7",
                  "size_bytes": 32152673
                }
              ]
            },
            {
              "id": "llama-cpp-whisper-medium-gguf",
              "name": "Whisper Medium (Q5_0 GGUF) - llama.cpp / whisper.cpp",
              "description": "High-accuracy multilingual Whisper Medium model (~539 MB). Ideal for multi-speaker meetings and nuanced domain vocabulary.",
              "model_type": "asr",
              "language_codes": ["multilingual", "en", "fa"],
              "device_tier_recommendation": "high",
              "size_bytes": 539212467,
              "quant_variant": "q5_0",
              "license": "MIT",
              "source_url": "https://huggingface.co/ggerganov/whisper.cpp",
              "measured_wer": null,
              "benchmark_notes": "GGUF Q5_0 quantized Medium model for whisper.cpp / llama.cpp high-efficiency inference.",
              "date_added": "2026-10-27",
              "files": [
                {
                  "filename": "ggml-medium-q5_0.bin",
                  "url": "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium-q5_0.bin",
                  "sha256": "19fea4b380c3a618ec4723c3eef2eb785ffba0d0538cf43f8f235e7b3b34220f",
                  "size_bytes": 539212467
                }
              ]
            },
            {
              "id": "llama-cpp-whisper-large-v3-turbo-gguf",
              "name": "Whisper Large v3 Turbo (Q5_0 GGUF) - llama.cpp / whisper.cpp",
              "description": "OpenAI's latest optimized Large-v3 Turbo model (~574 MB). Delivers near Large-v3 accuracy at 4x-6x faster decoding speed.",
              "model_type": "asr",
              "language_codes": ["multilingual", "en", "fa"],
              "device_tier_recommendation": "high",
              "size_bytes": 574041195,
              "quant_variant": "q5_0",
              "license": "MIT",
              "source_url": "https://huggingface.co/ggerganov/whisper.cpp",
              "measured_wer": null,
              "benchmark_notes": "GGUF Q5_0 quantized Large-v3 Turbo model for high-accuracy and high-throughput local inference.",
              "date_added": "2026-10-27",
              "files": [
                {
                  "filename": "ggml-large-v3-turbo-q5_0.bin",
                  "url": "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin",
                  "sha256": "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2",
                  "size_bytes": 574041195
                }
              ]
            },
            {
              "id": "sherpa-whisper-medium-int8",
              "name": "Whisper Medium (INT8) Multilingual",
              "description": "Sherpa-onnx Whisper Medium encoder/decoder quantized to INT8 with attention token timestamps for high-accuracy offline transcription.",
              "model_type": "asr",
              "language_codes": ["multilingual", "en", "fa"],
              "device_tier_recommendation": "high",
              "size_bytes": 946072270,
              "quant_variant": "int8",
              "license": "Apache-2.0",
              "source_url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-medium",
              "measured_wer": null,
              "benchmark_notes": "Sherpa-onnx export of Whisper Medium with encoder/decoder INT8 quantization.",
              "date_added": "2026-10-27",
              "files": [
                {
                  "filename": "medium-encoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-medium/resolve/main/medium-encoder.int8.onnx",
                  "sha256": "1c54582b4d829de0089f6cb63bbbdb3bf7555398bacaf855fbecf1a84dfd193e",
                  "size_bytes": 374196283
                },
                {
                  "filename": "medium-decoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-medium/resolve/main/medium-decoder.int8.onnx",
                  "sha256": "595d00a338a365a7bfa0ca7f296cabc639583bef770ab6130df90f49a6412747",
                  "size_bytes": 571059257
                },
                {
                  "filename": "medium-tokens.txt",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-medium/resolve/main/medium-tokens.txt",
                  "sha256": "b34b360dbb493e781e479794586d661700670d65564001f23024971d1f2fa126",
                  "size_bytes": 816730
                }
              ]
            },
            {
              "id": "sherpa-whisper-large-v3-int8",
              "name": "Whisper Large v3 (INT8) Multilingual",
              "description": "Maximum-accuracy multilingual Whisper Large-v3 model with INT8 quantization, supporting complex multilingual and Persian speech.",
              "model_type": "asr",
              "language_codes": ["multilingual", "en", "fa"],
              "device_tier_recommendation": "high",
              "size_bytes": 1775753918,
              "quant_variant": "int8",
              "license": "Apache-2.0",
              "source_url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-large-v3",
              "measured_wer": null,
              "benchmark_notes": "Sherpa-onnx export of Whisper Large-v3 with INT8 quantization.",
              "date_added": "2026-10-27",
              "files": [
                {
                  "filename": "large-v3-encoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-large-v3/resolve/main/large-v3-encoder.int8.onnx",
                  "sha256": "d531cf17248acc43e8c09b472a0877055e770877857a5332fc1304b36534ec85",
                  "size_bytes": 766671985
                },
                {
                  "filename": "large-v3-decoder.int8.onnx",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-large-v3/resolve/main/large-v3-decoder.int8.onnx",
                  "sha256": "ebc6bfd88e162a46cb3edee8a7e727e1dcbc65cabecb19e2573695e4d495e1af",
                  "size_bytes": 1008265203
                },
                {
                  "filename": "large-v3-tokens.txt",
                  "url": "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-large-v3/resolve/main/large-v3-tokens.txt",
                  "sha256": "b34b360dbb493e781e479794586d661700670d65564001f23024971d1f2fa126",
                  "size_bytes": 816730
                }
              ]
            }
          ]
        }
        """;
    }
}
