using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Xunit;

namespace Scriber.Inference.Asr.Tests;

public class LlamaCppAsrTests : IDisposable
{
    private readonly string _dummyModelPath;

    public LlamaCppAsrTests()
    {
        _dummyModelPath = Path.Combine(Path.GetTempPath(), $"dummy_model_{Guid.NewGuid():N}.gguf");
        File.WriteAllBytes(_dummyModelPath, [0x47, 0x47, 0x55, 0x46]); // GGUF magic header
    }

    public void Dispose()
    {
        try { if (File.Exists(_dummyModelPath)) File.Delete(_dummyModelPath); } catch { }
    }

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LlamaCppAsrBackend(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_EmptyModelPath_ThrowsArgumentException(string? modelPath)
    {
        var config = new LlamaCppConfig { ModelPath = modelPath! };
        Assert.ThrowsAny<ArgumentException>(() => new LlamaCppAsrBackend(config));
    }

    [Fact]
    public void Constructor_NonExistentModel_ThrowsFileNotFoundException()
    {
        var config = new LlamaCppConfig { ModelPath = "non_existent_file.gguf" };
        Assert.Throws<FileNotFoundException>(() => new LlamaCppAsrBackend(config));
    }

    [Fact]
    public void SupportedLanguages_ContainsAutoAndStandardCodes()
    {
        var config = new LlamaCppConfig { ModelPath = _dummyModelPath };
        using var backend = new LlamaCppAsrBackend(config);

        Assert.Contains("auto", backend.SupportedLanguageCodes);
        Assert.Contains("en", backend.SupportedLanguageCodes);
        Assert.Contains("fa", backend.SupportedLanguageCodes);
        Assert.Contains("es", backend.SupportedLanguageCodes);
        Assert.True(backend.SupportsStreaming);
    }

    [Fact]
    public async Task TranscribeAsync_InvalidSampleRate_ThrowsArgumentException()
    {
        var config = new LlamaCppConfig { ModelPath = _dummyModelPath };
        using var backend = new LlamaCppAsrBackend(config);

        var badAudio = new AudioBuffer([0f, 0f, 0f], 44100, 1);
        await Assert.ThrowsAsync<ArgumentException>(() => backend.TranscribeAsync(badAudio, new AsrOptions()));
    }

    [Fact]
    public async Task TranscribeAsync_InvalidChannels_ThrowsArgumentException()
    {
        var config = new LlamaCppConfig { ModelPath = _dummyModelPath };
        using var backend = new LlamaCppAsrBackend(config);

        var badAudio = new AudioBuffer([0f, 0f], 16000, 2);
        await Assert.ThrowsAsync<ArgumentException>(() => backend.TranscribeAsync(badAudio, new AsrOptions()));
    }

    [Fact]
    public async Task TranscribeAsync_Cancelled_ThrowsOperationCanceledException()
    {
        var config = new LlamaCppConfig { ModelPath = _dummyModelPath };
        using var backend = new LlamaCppAsrBackend(config);

        var validAudio = new AudioBuffer([0f, 0f, 0f], 16000, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.TranscribeAsync(validAudio, new AsrOptions(), cts.Token));
    }
}
