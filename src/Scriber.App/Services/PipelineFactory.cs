using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;
using Scriber.Pipeline;
using Scriber.Pipeline.Alignment;
using Scriber.Pipeline.Import;
using Scriber.Pipeline.Normalization;
using Scriber.Pipeline.Vad;
using Scriber.Storage.Models;

namespace Scriber.App.Services;

public interface IPipelineFactory
{
    ITranscriptionPipeline CreatePipeline();
}

public sealed class PipelineFactory : IPipelineFactory
{
    private readonly IModelCacheManager _cacheManager;

    public PipelineFactory(IModelCacheManager cacheManager)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
    }

    public ITranscriptionPipeline CreatePipeline()
    {
        var decoder = new FfmpegAudioDecoder();

        // 1. Locate Diarizer model
        string diarizerPath = FindModelFile("nemotron3-diarizer-v3", "nemotron3_stream_step.onnx");

        // 2. Locate Whisper ASR models
        string asrEncoderPath = FindModelFile("sherpa-whisper-small-int8", "small-encoder.int8.onnx");
        string asrDecoderPath = FindModelFile("sherpa-whisper-small-int8", "small-decoder.int8.onnx");
        string asrTokensPath = FindModelFile("sherpa-whisper-small-int8", "small-tokens.txt");

        // 3. Locate Silero VAD model (optional)
        string? vadPath = FindOptionalModelFile("snakers-silero-vad-v5", "silero_vad.onnx");

        if (string.IsNullOrEmpty(diarizerPath) || !File.Exists(diarizerPath))
            throw new FileNotFoundException("Nemotron Diarizer ONNX model not found. Please download it via Model Manager.");

        if (string.IsNullOrEmpty(asrEncoderPath) || !File.Exists(asrEncoderPath))
            throw new FileNotFoundException("Whisper ASR encoder ONNX model not found. Please download it via Model Manager.");

        string assetsDir = Path.GetDirectoryName(diarizerPath) ?? string.Empty;
        var diarizerConfig = new NemotronDiarizerConfig
        {
            ModelPath = diarizerPath,
            AssetsDir = assetsDir
        };
        var diarizer = new NemotronDiarizer(diarizerConfig);

        var asrConfig = new SherpaWhisperConfig
        {
            EncoderPath = asrEncoderPath,
            DecoderPath = asrDecoderPath,
            TokensPath = asrTokensPath
        };
        var asr = new SherpaWhisperAsrBackend(asrConfig);

        IVad? vad = null;
        if (!string.IsNullOrEmpty(vadPath) && File.Exists(vadPath))
        {
            vad = new SherpaVad(vadPath);
        }

        var aligner = new TranscriptAligner();
        var normalizer = new PersianTextNormalizer();

        return new TranscriptionPipeline(decoder, diarizer, asr, aligner, vad, normalizer);
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
            string inSpike2 = Path.Combine(repoRoot, "spikes", "PH0-SPIKE-002", "artifacts");
            if (File.Exists(Path.Combine(inSpike2, filename))) return Path.Combine(inSpike2, filename);
            if (File.Exists(Path.Combine(inSpike2, "whisper-export", filename))) return Path.Combine(inSpike2, "whisper-export", filename);

            string inFixtures = Path.Combine(repoRoot, "src", "Scriber.Pipeline.Tests", "fixtures", filename);
            if (File.Exists(inFixtures)) return inFixtures;
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
