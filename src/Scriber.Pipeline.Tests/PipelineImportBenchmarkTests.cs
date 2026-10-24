using Scriber.Core.Audio;
using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;
using Scriber.Pipeline.Alignment;
using Scriber.Pipeline.Import;
using Scriber.Pipeline.Normalization;
using Scriber.Pipeline.Vad;
using Xunit;
using Xunit.Abstractions;

namespace Scriber.Pipeline.Tests;

public class PipelineImportBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public PipelineImportBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (.git) not found");
    }

    private static string GetWhisperDir() =>
        Path.Combine(RepoRoot(), "spikes", "PH0-SPIKE-002", "artifacts", "whisper-export");

    private static string GetDiarizerPath() =>
        Path.Combine(RepoRoot(), "spikes", "PH0-SPIKE-001", "artifacts", "onnx", "nemotron3_stream_step.onnx");

    private static string GetVadPath() =>
        Path.Combine(RepoRoot(), "src", "Scriber.Pipeline.Tests", "fixtures", "silero_vad.onnx");

    [Fact]
    public async Task EndToEnd_Import_EnglishSample_WithPipelineCombination()
    {
        string samplePath = Path.Combine(RepoRoot(), "test_data", "audio", "english_sample_alice.wav");
        string gtPath = Path.Combine(RepoRoot(), "test_data", "audio", "english_sample_alice.groundtruth.txt");
        Assert.True(File.Exists(samplePath));

        var decoder = new FfmpegAudioDecoder();
        var asr = new SherpaWhisperAsrBackend(new SherpaWhisperConfig
        {
            EncoderPath = Path.Combine(GetWhisperDir(), "small-encoder.int8.onnx"),
            DecoderPath = Path.Combine(GetWhisperDir(), "small-decoder.int8.onnx"),
            TokensPath = Path.Combine(GetWhisperDir(), "small-tokens.txt"),
            Language = "auto"
        });

        IDiarizer diarizer;
        string diarizerPath = GetDiarizerPath();
        if (File.Exists(diarizerPath))
        {
            diarizer = new NemotronDiarizer(new NemotronDiarizerConfig
            {
                ModelPath = diarizerPath,
                AssetsDir = Path.Combine(RepoRoot(), "spikes", "PH0-SPIKE-001", "artifacts", "csharp-port-assets")
            });
        }
        else
        {
            diarizer = new PassthroughDiarizer();
        }

        IVad? vad = null;
        string vadPath = GetVadPath();
        if (File.Exists(vadPath))
        {
            vad = new SherpaVad(vadPath);
        }

        var aligner = new TranscriptAligner();
        var normalizer = new PersianTextNormalizer();

        var pipeline = new TranscriptionPipeline(
            decoder,
            diarizer,
            asr,
            aligner,
            vad,
            normalizer);

        var options = new TranscriptionPipelineOptions
        {
            Title = "English Audio Import Benchmark",
            LanguageCode = "auto",
            EnableVad = true
        };

        var transcript = await pipeline.ProcessAsync(samplePath, options);

        Assert.NotNull(transcript);
        Assert.NotEmpty(transcript.Segments);

        string recognized = string.Join(" ", transcript.Segments.Select(s => s.Text)).ToLowerInvariant();
        string groundTruth = (await File.ReadAllTextAsync(gtPath)).ToLowerInvariant().Trim();

        _output.WriteLine("=== ENGLISH PIPELINE IMPORT RESULT ===");
        _output.WriteLine($"Ground Truth: {groundTruth}");
        _output.WriteLine($"Recognized  : {recognized}");
        _output.WriteLine($"Speakers    : {transcript.Speakers.Count}");
        _output.WriteLine($"Total Words : {transcript.Segments.Sum(s => s.Words.Count)}");

        Assert.True(recognized.Contains("podcast") && recognized.Contains("clarify"));
    }

    [Fact]
    public async Task EndToEnd_Import_PersianSample_WithPipelineCombination()
    {
        string samplePath = Path.Combine(RepoRoot(), "test_data", "audio", "persian_sample_1.wav");
        string gtPath = Path.Combine(RepoRoot(), "test_data", "audio", "persian_sample_1.groundtruth.txt");
        Assert.True(File.Exists(samplePath));

        var decoder = new FfmpegAudioDecoder();
        var asr = new SherpaWhisperAsrBackend(new SherpaWhisperConfig
        {
            EncoderPath = Path.Combine(GetWhisperDir(), "small-encoder.int8.onnx"),
            DecoderPath = Path.Combine(GetWhisperDir(), "small-decoder.int8.onnx"),
            TokensPath = Path.Combine(GetWhisperDir(), "small-tokens.txt"),
            Language = "fa"
        });

        IDiarizer diarizer = new PassthroughDiarizer();
        IVad? vad = File.Exists(GetVadPath()) ? new SherpaVad(GetVadPath()) : null;
        var aligner = new TranscriptAligner();
        var normalizer = new PersianTextNormalizer();

        var pipeline = new TranscriptionPipeline(
            decoder,
            diarizer,
            asr,
            aligner,
            vad,
            normalizer);

        var options = new TranscriptionPipelineOptions
        {
            Title = "Persian Audio Import Benchmark",
            LanguageCode = "fa",
            EnableVad = true
        };

        var transcript = await pipeline.ProcessAsync(samplePath, options);

        Assert.NotNull(transcript);
        Assert.NotEmpty(transcript.Segments);

        string recognized = string.Join(" ", transcript.Segments.Select(s => s.Text));
        string groundTruth = (await File.ReadAllTextAsync(gtPath)).Trim();

        _output.WriteLine("=== PERSIAN PIPELINE IMPORT RESULT ===");
        _output.WriteLine($"Ground Truth: {groundTruth}");
        _output.WriteLine($"Recognized  : {recognized}");
        _output.WriteLine($"Speakers    : {transcript.Speakers.Count}");
        _output.WriteLine($"Total Words : {transcript.Segments.Sum(s => s.Words.Count)}");

        Assert.True(recognized.Contains("بیشتر") || recognized.Contains("احت") || recognized.Contains("سکوت"));
    }
}
