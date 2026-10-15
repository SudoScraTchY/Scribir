using Scriber.Core.Audio;
using Scriber.Pipeline.Import;
using Xunit;

namespace Scriber.Inference.Asr.Tests;

public class SherpaWhisperAsrTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (.git) not found");
    }

    private static string GetWhisperDir() =>
        Path.Combine(RepoRoot(), "spikes", "PH0-SPIKE-002", "artifacts", "whisper-export");

    private static string GetFixturePath(string name) =>
        Path.Combine(RepoRoot(), "src", "Scriber.Pipeline.Tests", "fixtures", name);

    private static SherpaWhisperConfig CreateDefaultConfig() =>
        new()
        {
            EncoderPath = Path.Combine(GetWhisperDir(), "small-encoder.int8.onnx"),
            DecoderPath = Path.Combine(GetWhisperDir(), "small-decoder.int8.onnx"),
            TokensPath = Path.Combine(GetWhisperDir(), "small-tokens.txt"),
            Language = "en"
        };

    private static SherpaWhisperAsrBackend CreateBackend() =>
        new(CreateDefaultConfig());

    [Fact]
    public void Constructor_MissingFiles_ThrowsFileNotFoundException()
    {
        var badEncoder = new SherpaWhisperConfig
        {
            EncoderPath = "nonexistent_encoder.onnx",
            DecoderPath = Path.Combine(GetWhisperDir(), "small-decoder.int8.onnx"),
            TokensPath = Path.Combine(GetWhisperDir(), "small-tokens.txt")
        };
        Assert.Throws<FileNotFoundException>(() => new SherpaWhisperAsrBackend(badEncoder));

        var badDecoder = new SherpaWhisperConfig
        {
            EncoderPath = Path.Combine(GetWhisperDir(), "small-encoder.int8.onnx"),
            DecoderPath = "nonexistent_decoder.onnx",
            TokensPath = Path.Combine(GetWhisperDir(), "small-tokens.txt")
        };
        Assert.Throws<FileNotFoundException>(() => new SherpaWhisperAsrBackend(badDecoder));

        var badTokens = new SherpaWhisperConfig
        {
            EncoderPath = Path.Combine(GetWhisperDir(), "small-encoder.int8.onnx"),
            DecoderPath = Path.Combine(GetWhisperDir(), "small-decoder.int8.onnx"),
            TokensPath = "nonexistent_tokens.txt"
        };
        Assert.Throws<FileNotFoundException>(() => new SherpaWhisperAsrBackend(badTokens));
    }

    [Fact]
    public void Metadata_ReportsPropertiesAccurately()
    {
        using var backend = CreateBackend();

        Assert.Equal("sherpa-whisper-small-int8", backend.Id);
        Assert.Contains("Whisper", backend.DisplayName);
        Assert.False(backend.SupportsStreaming, "Offline Whisper backend must report SupportsStreaming=false");
        Assert.Contains("en", backend.SupportedLanguageCodes);
        Assert.Contains("fa", backend.SupportedLanguageCodes);
        Assert.True(backend.SupportedLanguageCodes.Count >= 90);
    }

    [Fact]
    public async Task TranscribeStreamingAsync_WhenStreamingUnsupported_ThrowsNotSupportedException()
    {
        using var backend = CreateBackend();
        var emptyChunks = EmptyChunks();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in backend.TranscribeStreamingAsync(emptyChunks, new AsrOptions()))
            {
            }
        });

        static async IAsyncEnumerable<AudioChunk> EmptyChunks()
        {
            yield break;
        }
    }

    [Fact]
    public async Task TranscribeAsync_InvalidAudio_ThrowsArgumentException()
    {
        using var backend = CreateBackend();
        var options = new AsrOptions();

        // Stereo audio rejected
        var stereo = new AudioBuffer(new float[16000], 16000, 2);
        await Assert.ThrowsAsync<ArgumentException>(() => backend.TranscribeAsync(stereo, options));

        // Wrong sample rate rejected
        var wrongRate = new AudioBuffer(new float[8000], 8000, 1);
        await Assert.ThrowsAsync<ArgumentException>(() => backend.TranscribeAsync(wrongRate, options));
    }

    [Fact]
    public async Task TranscribeAsync_Cancellation_ThrowsOperationCanceledException()
    {
        using var backend = CreateBackend();
        var audio = new AudioBuffer(new float[16000], 16000, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            backend.TranscribeAsync(audio, new AsrOptions(), cts.Token));
    }

    [Fact]
    public async Task TranscribeAsync_TwoSpeakerFixture_ReturnsWordsWithTimestampsAndConfidence()
    {
        var decoder = new FfmpegAudioDecoder();
        var audio = await decoder.DecodeAsync(GetFixturePath("two_speaker_fixture.wav"));

        using var backend = CreateBackend();
        var options = new AsrOptions { LanguageCode = "en" };

        var segments = await backend.TranscribeAsync(audio, options);

        Assert.NotEmpty(segments);

        // Flatten all words across all segments
        var allWords = segments.SelectMany(s => s.Words).ToList();
        Assert.NotEmpty(allWords);

        // Verify word timestamps and confidence values
        foreach (var word in allWords)
        {
            Assert.False(string.IsNullOrWhiteSpace(word.Text));
            Assert.True(word.End >= word.Start, $"Word '{word.Text}' End < Start");
            Assert.InRange(word.Confidence, 0.0, 1.0);
        }

        // Verify chronological ordering of words
        for (int i = 1; i < allWords.Count; i++)
        {
            Assert.True(allWords[i].Start >= allWords[i - 1].Start,
                $"Word '{allWords[i].Text}' at {allWords[i].Start} precedes previous word '{allWords[i - 1].Text}' at {allWords[i - 1].Start}");
        }

        // Combine text and verify recognizable speech from the fixture
        var fullText = string.Join(" ", segments.Select(s => s.Text)).ToLowerInvariant();
        Assert.True(fullText.Contains("podcast") || fullText.Contains("clarify") || fullText.Contains("stories"),
            $"Transcript text '{fullText}' did not contain expected fixture vocabulary.");
    }
}
