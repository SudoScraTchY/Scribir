namespace Scriber.Inference.Diarization;

/// <summary>
/// Operating point for the diarization inference pass.
/// </summary>
public enum DiarizationMode
{
    /// <summary>
    /// Streaming operating point (low latency, smaller chunks).
    /// </summary>
    Streaming,

    /// <summary>
    /// Offline operating point (larger chunk context, higher batch throughput).
    /// </summary>
    Offline
}

/// <summary>
/// Configuration options passed to IDiarizer.DiarizeAsync.
/// </summary>
public sealed record DiarizationOptions
{
    /// <summary>Operating mode: streaming (low latency) or offline (batch).</summary>
    public DiarizationMode Mode { get; init; } = DiarizationMode.Streaming;

    /// <summary>Probability threshold (0.0 to 1.0) above which a speaker is considered active.</summary>
    public float Threshold { get; init; } = 0.5f;

    /// <summary>Maximum number of speakers supported by the model architecture (default 8).</summary>
    public int NumSpeakers { get; init; } = 8;

    /// <summary>Prefix for assigned speaker IDs (e.g. "speaker-", resulting in "speaker-0", "speaker-1").</summary>
    public string SpeakerIdPrefix { get; init; } = "speaker-";
}
