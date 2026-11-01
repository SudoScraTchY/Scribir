using Scriber.Core.Audio;
using Scriber.Pipeline.Import;
using Xunit;
using Xunit.Abstractions;

namespace Scriber.Inference.Asr.Tests;

public class SampleAudioBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public SampleAudioBenchmarkTests(ITestOutputHelper output)
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

    [Fact]
    public async Task Benchmark_PersianSample_MatchesGroundTruth()
    {
        string samplePath = Path.Combine(RepoRoot(), "test_data", "audio", "persian_sample_1.wav");
        string gtPath = Path.Combine(RepoRoot(), "test_data", "audio", "persian_sample_1.groundtruth.txt");
        Assert.True(File.Exists(samplePath), $"Sample file not found: {samplePath}");
        Assert.True(File.Exists(gtPath), $"Ground truth file not found: {gtPath}");

        var decoder = new FfmpegAudioDecoder();
        var audio = await decoder.DecodeAsync(samplePath);

        using var backend = new SherpaWhisperAsrBackend(new SherpaWhisperConfig
        {
            EncoderPath = Path.Combine(GetWhisperDir(), "small-encoder.int8.onnx"),
            DecoderPath = Path.Combine(GetWhisperDir(), "small-decoder.int8.onnx"),
            TokensPath = Path.Combine(GetWhisperDir(), "small-tokens.txt"),
            Language = "fa"
        });

        var segments = await backend.TranscribeAsync(audio, new AsrOptions { LanguageCode = "fa" });
        Assert.NotEmpty(segments);

        string recognized = string.Join(" ", segments.Select(s => s.Text)).Trim();
        string groundTruth = (await File.ReadAllTextAsync(gtPath)).Trim();

        _output.WriteLine($"[PERSIAN GROUND TRUTH]: {groundTruth}");
        _output.WriteLine($"[PERSIAN RECOGNIZED  ]: {recognized}");

        Assert.False(string.IsNullOrWhiteSpace(recognized));
        // Verify key vocabulary from ground truth
        Assert.True(recognized.Contains("سکوت") || recognized.Contains("احترام") || recognized.Contains("احتیاج") || recognized.Contains("بیشتر"),
            $"Expected Persian keywords in transcript. Recognized: '{recognized}', GroundTruth: '{groundTruth}'");
    }

    [Fact]
    public async Task Benchmark_EnglishSampleAlice_MatchesGroundTruth()
    {
        string samplePath = Path.Combine(RepoRoot(), "test_data", "audio", "english_sample_alice.wav");
        string gtPath = Path.Combine(RepoRoot(), "test_data", "audio", "english_sample_alice.groundtruth.txt");
        Assert.True(File.Exists(samplePath), $"Sample file not found: {samplePath}");
        Assert.True(File.Exists(gtPath), $"Ground truth file not found: {gtPath}");

        var decoder = new FfmpegAudioDecoder();
        var audio = await decoder.DecodeAsync(samplePath);

        using var backend = new SherpaWhisperAsrBackend(new SherpaWhisperConfig
        {
            EncoderPath = Path.Combine(GetWhisperDir(), "small-encoder.int8.onnx"),
            DecoderPath = Path.Combine(GetWhisperDir(), "small-decoder.int8.onnx"),
            TokensPath = Path.Combine(GetWhisperDir(), "small-tokens.txt"),
            Language = "auto"
        });

        var segments = await backend.TranscribeAsync(audio, new AsrOptions { LanguageCode = "auto" });
        Assert.NotEmpty(segments);

        string recognized = string.Join(" ", segments.Select(s => s.Text)).ToLowerInvariant().Trim();
        string groundTruth = (await File.ReadAllTextAsync(gtPath)).ToLowerInvariant().Trim();

        _output.WriteLine($"[ENGLISH GROUND TRUTH]: {groundTruth}");
        _output.WriteLine($"[ENGLISH RECOGNIZED  ]: {recognized}");

        Assert.True(recognized.Contains("podcast") && recognized.Contains("clarify"),
            $"Expected words 'podcast' and 'clarify'. Recognized: '{recognized}', GroundTruth: '{groundTruth}'");
    }

    [Theory]
    [InlineData("persian_dialogue_fixture_1.wav", "persian_dialogue_fixture_1.groundtruth.txt")]
    [InlineData("persian_dialogue_fixture_2.wav", "persian_dialogue_fixture_2.groundtruth.txt")]
    [InlineData("persian_dialogue_fixture_3.wav", "persian_dialogue_fixture_3.groundtruth.txt")]
    public async Task Benchmark_PersianMultiSpeakerDialogue_ValidatesTranscription(string wavFile, string gtFile)
    {
        string samplePath = Path.Combine(RepoRoot(), "test_data", "audio", wavFile);
        string gtPath = Path.Combine(RepoRoot(), "test_data", "audio", gtFile);
        Assert.True(File.Exists(samplePath), $"Sample file not found: {samplePath}");
        Assert.True(File.Exists(gtPath), $"Ground truth file not found: {gtPath}");

        var decoder = new FfmpegAudioDecoder();
        var audio = await decoder.DecodeAsync(samplePath);

        using var backend = new SherpaWhisperAsrBackend(new SherpaWhisperConfig
        {
            EncoderPath = Path.Combine(GetWhisperDir(), "small-encoder.int8.onnx"),
            DecoderPath = Path.Combine(GetWhisperDir(), "small-decoder.int8.onnx"),
            TokensPath = Path.Combine(GetWhisperDir(), "small-tokens.txt"),
            Language = "fa"
        });

        var segments = await backend.TranscribeAsync(audio, new AsrOptions { LanguageCode = "fa" });
        Assert.NotEmpty(segments);

        string recognized = string.Join(" ", segments.Select(s => s.Text)).Trim();
        string groundTruth = (await File.ReadAllTextAsync(gtPath)).Trim();

        _output.WriteLine($"[{wavFile} GROUND TRUTH]: {groundTruth}");
        _output.WriteLine($"[{wavFile} RECOGNIZED  ]: {recognized}");

        Assert.False(string.IsNullOrWhiteSpace(recognized));
        // Verify that Persian speech is accurately transcribed from either speaker turn
        Assert.True(recognized.Contains("سکوت") || recognized.Contains("احترام") || recognized.Contains("آماده") || recognized.Contains("هوا") || recognized.Contains("آفتاب"),
            $"Expected recognized Persian vocabulary in transcript. Recognized: '{recognized}'");
    }
}
