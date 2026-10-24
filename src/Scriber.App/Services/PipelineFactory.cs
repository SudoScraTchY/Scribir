using Scriber.Core.Domain;
using Scriber.Core.Inference;
using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;
using Scriber.Inference.SpeakerId;
using Scriber.Pipeline;
using Scriber.Pipeline.Alignment;
using Scriber.Pipeline.Import;
using Scriber.Pipeline.Normalization;
using Scriber.Pipeline.Streaming;
using Scriber.Pipeline.Vad;
using Scriber.Storage;
using Scriber.Storage.Models;

namespace Scriber.App.Services;

public interface IPipelineFactory
{
    ITranscriptionPipeline CreatePipeline(TranscriptionPipelineOptions? options = null);
    ILiveTranscriptionSession CreateLiveSession(TranscriptionPipelineOptions? options = null);
    ITwoPassReconciler CreateReconciler();
}

public sealed class PipelineFactory : IPipelineFactory
{
    private readonly IModelCacheManager _cacheManager;
    private readonly IVoiceprintRepository? _voiceprintRepo;
    private readonly ISpeakerEmbedder? _speakerEmbedder;
    private readonly SpeakerMatcher? _speakerMatcher;

    public PipelineFactory(
        IModelCacheManager cacheManager,
        IVoiceprintRepository? voiceprintRepo = null,
        ISpeakerEmbedder? speakerEmbedder = null,
        SpeakerMatcher? speakerMatcher = null)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _voiceprintRepo = voiceprintRepo;
        _speakerEmbedder = speakerEmbedder;
        _speakerMatcher = speakerMatcher;
    }

    public ITranscriptionPipeline CreatePipeline(TranscriptionPipelineOptions? options = null)
    {
        options ??= new TranscriptionPipelineOptions();
        var decoder = new FfmpegAudioDecoder();

        // 1. Locate Diarizer model (or fallback to PassthroughDiarizer)
        string diarizerPath = FindOptionalModelFile("nemotron3-diarizer-v3", "nemotron3_stream_step.onnx") ?? string.Empty;

        // 2. Locate or instantiate ASR backend depending on selected ModelId
        IAsrBackend asr = CreateAsrBackend(options);

        // 3. Locate Silero VAD model (optional)
        string? vadPath = FindOptionalModelFile("snakers-silero-vad-v5", "silero_vad.onnx");

        IDiarizer diarizer;
        if (options.EnableDiarization && !string.IsNullOrEmpty(diarizerPath) && File.Exists(diarizerPath))
        {
            string assetsDir = Path.GetDirectoryName(diarizerPath) ?? string.Empty;
            if (!File.Exists(Path.Combine(assetsDir, "mel_filters_128x257.bin")))
            {
                string repoRoot = FindRepoRoot();
                if (!string.IsNullOrEmpty(repoRoot))
                {
                    string portAssets = Path.Combine(repoRoot, "spikes", "PH0-SPIKE-001", "artifacts", "csharp-port-assets");
                    if (Directory.Exists(portAssets) && File.Exists(Path.Combine(portAssets, "mel_filters_128x257.bin")))
                    {
                        assetsDir = portAssets;
                    }
                }
            }

            var diarizerConfig = new NemotronDiarizerConfig
            {
                ModelPath = diarizerPath,
                AssetsDir = assetsDir
            };
            diarizer = new NemotronDiarizer(diarizerConfig);
        }
        else
        {
            diarizer = new PassthroughDiarizer();
        }

        IVad? vad = null;
        if (options.EnableVad && !string.IsNullOrEmpty(vadPath) && File.Exists(vadPath))
        {
            vad = new SherpaVad(vadPath);
        }

        var aligner = new TranscriptAligner();
        var normalizer = new PersianTextNormalizer();

        // 4. Voiceprint feature cross-session integration
        IReadOnlyList<Voiceprint>? voiceprints = null;
        if (options.EnableVoiceprints)
        {
            try
            {
                voiceprints = _voiceprintRepo?.GetAllAsync().GetAwaiter().GetResult();
            }
            catch { }
        }

        var embedder = options.EnableVoiceprints ? ResolveSpeakerEmbedder() : null;

        return new TranscriptionPipeline(
            decoder,
            diarizer,
            asr,
            aligner,
            vad,
            normalizer,
            embedder,
            options.EnableVoiceprints ? _speakerMatcher : null,
            voiceprints);
    }

    private ISpeakerEmbedder ResolveSpeakerEmbedder()
    {
        string embedderPath = FindModelFile("3dspeaker-eres2net-base", "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx");
        if (File.Exists(embedderPath))
        {
            try
            {
                return new SherpaSpeakerEmbedder(new SherpaSpeakerEmbedderConfig
                {
                    ModelPath = embedderPath,
                    NumThreads = 2
                });
            }
            catch { }
        }

        return _speakerEmbedder ?? new InMemorySpeakerEmbedder();
    }

    private IAsrBackend CreateAsrBackend(TranscriptionPipelineOptions options)
    {
        string modelId = options.ModelId;

        // Check if llama.cpp / whisper.cpp backend is requested
        if (string.Equals(modelId, "llama-cpp-whisper-base-gguf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(modelId, "llama-cpp-whisper-small-gguf", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("llama-cpp", StringComparison.OrdinalIgnoreCase))
        {
            string binName = modelId.Contains("small", StringComparison.OrdinalIgnoreCase)
                ? "ggml-small-q5_1.bin"
                : "ggml-base-q5_1.bin";

            string ggufPath = FindOptionalModelFile(modelId, binName)
                ?? Path.Combine(_cacheManager.CacheDirectory, modelId, binName);

            var llamaConfig = new LlamaCppConfig
            {
                ModelPath = ggufPath,
                Id = modelId,
                DisplayName = modelId.Contains("small", StringComparison.OrdinalIgnoreCase)
                    ? "Whisper Small (GGUF) via llama.cpp / whisper.cpp"
                    : "Whisper Base (GGUF) via llama.cpp / whisper.cpp"
            };

            if (File.Exists(ggufPath))
            {
                return new LlamaCppAsrBackend(llamaConfig);
            }
        }

        // Check specific ONNX Whisper variants
        string encoderName = "small-encoder.int8.onnx";
        string decoderName = "small-decoder.int8.onnx";
        string tokensName = "small-tokens.txt";

        if (string.Equals(modelId, "sherpa-whisper-tiny-int8", StringComparison.OrdinalIgnoreCase))
        {
            encoderName = "tiny-encoder.int8.onnx";
            decoderName = "tiny-decoder.int8.onnx";
            tokensName = "tiny-tokens.txt";
        }
        else if (string.Equals(modelId, "sherpa-whisper-base-int8", StringComparison.OrdinalIgnoreCase))
        {
            encoderName = "base-encoder.int8.onnx";
            decoderName = "base-decoder.int8.onnx";
            tokensName = "base-tokens.txt";
        }

        string asrEncoderPath = FindModelFile(modelId, encoderName);
        string asrDecoderPath = FindModelFile(modelId, decoderName);
        string asrTokensPath = FindModelFile(modelId, tokensName);

        // Fallback to default small if specific variant not downloaded yet
        if (!File.Exists(asrEncoderPath) || !File.Exists(asrDecoderPath) || !File.Exists(asrTokensPath))
        {
            modelId = "sherpa-whisper-small-int8";
            asrEncoderPath = FindModelFile(modelId, "small-encoder.int8.onnx");
            asrDecoderPath = FindModelFile(modelId, "small-decoder.int8.onnx");
            asrTokensPath = FindModelFile(modelId, "small-tokens.txt");
        }

        if (string.IsNullOrEmpty(asrEncoderPath) || !File.Exists(asrEncoderPath))
            throw new FileNotFoundException($"Whisper ASR encoder ONNX model not found: {asrEncoderPath}. Please download it via Model Manager.");

        string lang = string.Equals(options.LanguageCode, "auto", StringComparison.OrdinalIgnoreCase) ? "" : options.LanguageCode;
        var asrConfig = new SherpaWhisperConfig
        {
            EncoderPath = asrEncoderPath,
            DecoderPath = asrDecoderPath,
            TokensPath = asrTokensPath,
            Language = lang
        };

        return new SherpaWhisperAsrBackend(asrConfig);
    }

    public ITwoPassReconciler CreateReconciler() => new TwoPassReconciler();

    public ILiveTranscriptionSession CreateLiveSession(TranscriptionPipelineOptions? options = null)
    {
        options ??= new TranscriptionPipelineOptions();
        try
        {
            var asr = CreateAsrBackend(options);
            string diarizerPath = FindOptionalModelFile("nemotron3-diarizer-v3", "nemotron3_stream_step.onnx") ?? string.Empty;
            IDiarizer diarizer;
            if (options.EnableDiarization && !string.IsNullOrEmpty(diarizerPath) && File.Exists(diarizerPath))
            {
                string assetsDir = Path.GetDirectoryName(diarizerPath) ?? string.Empty;
                diarizer = new NemotronDiarizer(new NemotronDiarizerConfig { ModelPath = diarizerPath, AssetsDir = assetsDir });
            }
            else
            {
                diarizer = new PassthroughDiarizer();
            }

            return new LiveTranscriptionSession(asr, diarizer, options);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live session fallback: {ex.Message}");
            return new SimulatedLiveTranscriptionSession(options);
        }
    }

    private string FindModelFile(string modelId, string filename)
    {
        // 1. Check local model cache directory
        string inCache = Path.Combine(_cacheManager.CacheDirectory, modelId, filename);
        if (File.Exists(inCache)) return inCache;

        // 2. Check repo spikes/artifacts fallback (for development)
        string repoRoot = FindRepoRoot();
        if (!string.IsNullOrEmpty(repoRoot))
        {
            string inSpike1 = Path.Combine(repoRoot, "spikes", "PH0-SPIKE-001", "artifacts");
            if (File.Exists(Path.Combine(inSpike1, "onnx", filename))) return Path.Combine(inSpike1, "onnx", filename);
            if (File.Exists(Path.Combine(inSpike1, "sherpa-models", filename))) return Path.Combine(inSpike1, "sherpa-models", filename);
            if (File.Exists(Path.Combine(inSpike1, filename))) return Path.Combine(inSpike1, filename);

            string inSpike2 = Path.Combine(repoRoot, "spikes", "PH0-SPIKE-002", "artifacts");
            if (File.Exists(Path.Combine(inSpike2, filename))) return Path.Combine(inSpike2, filename);
            if (File.Exists(Path.Combine(inSpike2, "whisper-export", filename))) return Path.Combine(inSpike2, "whisper-export", filename);

            string inFixtures = Path.Combine(repoRoot, "src", "Scriber.Pipeline.Tests", "fixtures", filename);
            if (File.Exists(Path.Combine(inFixtures, filename))) return Path.Combine(inFixtures, filename);
        }

        return inCache;
    }

    private string? FindOptionalModelFile(string modelId, string filename)
    {
        try { return FindModelFile(modelId, filename); } catch { return null; }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? string.Empty;
    }
}
