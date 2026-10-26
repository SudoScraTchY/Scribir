namespace Scriber.Pipeline.Benchmark;

public enum DeviceTier
{
    Low,
    Medium,
    High
}

public sealed record DeviceHardwareProfile(
    string Platform,
    string Architecture,
    int ProcessorCount,
    long EstimatedMemoryBytes,
    DeviceTier DetectedTier
);

public sealed record ModelBenchmarkScore(
    string ModelId,
    string ModelName,
    string Tier,
    double ExecutionTimeSeconds,
    double RealTimeFactor,
    long PeakMemoryDeltaBytes
);

public sealed record DeviceBenchmarkResult(
    DateTimeOffset Timestamp,
    DeviceHardwareProfile Hardware,
    IReadOnlyList<ModelBenchmarkScore> CandidateScores,
    string RecommendedModelId,
    string RecommendedDiarizerId,
    string Summary
);

public interface IDeviceBenchmarkService
{
    DeviceHardwareProfile DetectHardware();
    Task<DeviceBenchmarkResult> RunBenchmarkAsync(CancellationToken cancellationToken = default);
}
