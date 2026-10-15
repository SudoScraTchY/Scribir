namespace Scriber.Pipeline.Vad;

/// <summary>
/// Tuning for the Silero VAD. Defaults are the sherpa-onnx defaults, which
/// match the spike-002 validated configuration.
/// </summary>
public sealed record VadOptions
{
    /// <summary>Speech probability threshold (0–1).</summary>
    public float Threshold { get; init; } = 0.5f;

    /// <summary>Silence (seconds) required to close a speech segment.</summary>
    public float MinSilenceDuration { get; init; } = 0.25f;

    /// <summary>Minimum speech length (seconds) to open a segment.</summary>
    public float MinSpeechDuration { get; init; } = 0.25f;

    /// <summary>Analysis window in samples at 16 kHz. sherpa requires 512 for silero v5+.</param>
    public int WindowSize { get; init; } = 512;

    /// <summary>Maximum segment length (seconds) before sherpa force-splits.</summary>
    public float MaxSpeechDuration { get; init; } = 20f;
}