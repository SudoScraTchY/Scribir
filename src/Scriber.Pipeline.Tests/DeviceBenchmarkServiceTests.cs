namespace Scriber.Pipeline.Tests;

using Scriber.Pipeline.Benchmark;
using Xunit;

public class DeviceBenchmarkServiceTests
{
    [Fact]
    public void DetectHardware_ReturnsValidHardwareProfile()
    {
        var service = new DeviceBenchmarkService();
        var hardware = service.DetectHardware();

        Assert.NotNull(hardware);
        Assert.False(string.IsNullOrWhiteSpace(hardware.Platform));
        Assert.False(string.IsNullOrWhiteSpace(hardware.Architecture));
        Assert.True(hardware.ProcessorCount > 0);
        Assert.True(hardware.EstimatedMemoryBytes > 0);
        Assert.Contains(hardware.DetectedTier, new[] { DeviceTier.Low, DeviceTier.Medium, DeviceTier.High });
    }

    [Fact]
    public async Task RunBenchmarkAsync_ProducesRecommendationsAndCandidateScores()
    {
        var service = new DeviceBenchmarkService();
        var result = await service.RunBenchmarkAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.Hardware);
        Assert.Equal(3, result.CandidateScores.Count);

        var candidateIds = result.CandidateScores.Select(c => c.ModelId).ToList();
        Assert.Contains("sherpa-whisper-tiny-int8", candidateIds);
        Assert.Contains("sherpa-whisper-base-int8", candidateIds);
        Assert.Contains("sherpa-whisper-small-int8", candidateIds);

        foreach (var score in result.CandidateScores)
        {
            Assert.True(score.ExecutionTimeSeconds > 0);
            Assert.True(score.RealTimeFactor > 0);
            Assert.True(score.PeakMemoryDeltaBytes >= 0);
        }

        Assert.False(string.IsNullOrWhiteSpace(result.RecommendedModelId));
        Assert.Equal("nemotron3-diarizer-v3", result.RecommendedDiarizerId);
        Assert.Contains(result.Hardware.DetectedTier.ToString(), result.Summary);
    }

    [Fact]
    public async Task RunBenchmarkAsync_WithCancellationToken_CancelsPromptly()
    {
        var service = new DeviceBenchmarkService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await service.RunBenchmarkAsync(cts.Token);
        });
    }
}
