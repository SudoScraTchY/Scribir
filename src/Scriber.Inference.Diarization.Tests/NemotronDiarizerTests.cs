using Scriber.Core.Audio;
using Scriber.Pipeline.Import;
using Xunit;

namespace Scriber.Inference.Diarization.Tests;

public class NemotronDiarizerTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (.git) not found");
    }

    private static string GetModelPath() =>
        Path.Combine(RepoRoot(), "spikes", "PH0-SPIKE-001", "artifacts", "onnx", "nemotron3_stream_step.onnx");

    private static string GetAssetsDir() =>
        Path.Combine(RepoRoot(), "spikes", "PH0-SPIKE-001", "artifacts", "csharp-port-assets");

    private static string GetFixturePath(string name) =>
        Path.Combine(RepoRoot(), "src", "Scriber.Pipeline.Tests", "fixtures", name);

    private static NemotronDiarizer CreateDiarizer() =>
        new(GetModelPath(), GetAssetsDir());

    [Fact]
    public void Constructor_MissingModel_ThrowsFileNotFoundException()
    {
        var missingModel = Path.Combine(GetAssetsDir(), "nonexistent.onnx");
        Assert.Throws<FileNotFoundException>(() => new NemotronDiarizer(missingModel, GetAssetsDir()));
    }

    [Fact]
    public void Constructor_MissingAssetsDir_ThrowsDirectoryNotFoundException()
    {
        var missingAssets = Path.Combine(RepoRoot(), "nonexistent_dir");
        Assert.Throws<DirectoryNotFoundException>(() => new NemotronDiarizer(GetModelPath(), missingAssets));
    }

    [Fact]
    public async Task DiarizeAsync_InvalidAudio_ThrowsArgumentException()
    {
        using var diarizer = CreateDiarizer();
        var options = new DiarizationOptions();

        // Stereo audio rejected
        var stereo = new AudioBuffer(new float[16000], 16000, 2);
        await Assert.ThrowsAsync<ArgumentException>(() => diarizer.DiarizeAsync(stereo, options));

        // Non-16kHz audio rejected
        var wrongRate = new AudioBuffer(new float[8000], 8000, 1);
        await Assert.ThrowsAsync<ArgumentException>(() => diarizer.DiarizeAsync(wrongRate, options));
    }

    [Fact]
    public async Task DiarizeAsync_SubChunkAudio_ReturnsEmptyList()
    {
        using var diarizer = CreateDiarizer();
        var shortAudio = new AudioBuffer(new float[160], 16000, 1);

        var turns = await diarizer.DiarizeAsync(shortAudio, new DiarizationOptions());

        Assert.Empty(turns);
    }

    [Fact]
    public async Task DiarizeAsync_Cancellation_ThrowsOperationCanceledException()
    {
        using var diarizer = CreateDiarizer();
        var audio = new AudioBuffer(new float[16000], 16000, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            diarizer.DiarizeAsync(audio, new DiarizationOptions(), cts.Token));
    }

    [Fact]
    public async Task DiarizeAsync_TwoSpeakerFixture_RecoversEightTurnsWithStableSpeakerIds()
    {
        var decoder = new FfmpegAudioDecoder();
        var audio = await decoder.DecodeAsync(GetFixturePath("two_speaker_fixture.wav"));

        using var diarizer = CreateDiarizer();
        var options = new DiarizationOptions
        {
            Mode = DiarizationMode.Streaming,
            Threshold = 0.5f,
            SpeakerIdPrefix = "speaker-"
        };

        var turns = await diarizer.DiarizeAsync(audio, options);

        // Ground truth: 8 alternating speech turns between 2 speakers
        Assert.Equal(8, turns.Count);

        // First speaker must be speaker-0 (ordered by first appearance)
        Assert.Equal("speaker-0", turns[0].SpeakerId);
        // Second speaker must be speaker-1
        Assert.Equal("speaker-1", turns[1].SpeakerId);

        // Alternation and ID stability
        for (int i = 0; i < turns.Count; i++)
        {
            var expectedSpeaker = i % 2 == 0 ? "speaker-0" : "speaker-1";
            Assert.Equal(expectedSpeaker, turns[i].SpeakerId);

            // Invariant: End > Start
            Assert.True(turns[i].End > turns[i].Start, $"Turn {i} End <= Start");
            Assert.True(turns[i].Duration.TotalSeconds >= 3.0, $"Turn {i} duration unexpectedly short");
        }

        // Expected turn boundaries (seconds) from spike 002 validation
        (double start, double end)[] expectedRanges =
        [
            (0.30, 5.87),
            (6.59, 11.55),
            (13.31, 18.87),
            (19.58, 24.54),
            (26.31, 31.87),
            (32.58, 37.55),
            (39.31, 44.87),
            (45.58, 50.54)
        ];

        for (int i = 0; i < 8; i++)
        {
            var (expStart, expEnd) = expectedRanges[i];
            Assert.InRange(turns[i].Start.TotalSeconds, expStart - 0.25, expStart + 0.25);
            Assert.InRange(turns[i].End.TotalSeconds, expEnd - 0.25, expEnd + 0.25);
        }
    }
}
