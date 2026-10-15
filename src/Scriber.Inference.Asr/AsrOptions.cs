namespace Scriber.Inference.Asr;

/// <summary>
/// Configuration options controlling an ASR transcription run.
/// </summary>
public sealed record AsrOptions
{
    /// <summary>Target language code (e.g. "en", "fa", "auto"). Default is "en".</summary>
    public string LanguageCode { get; init; } = "en";

    /// <summary>Whisper task: "transcribe" (default) or "translate".</summary>
    public string Task { get; init; } = "transcribe";

    /// <summary>Whether to request token-level timestamps from the model decoder.</summary>
    public bool EnableTokenTimestamps { get; init; } = true;

    /// <summary>Sampling temperature (0.0 for greedy search).</summary>
    public float Temperature { get; init; } = 0.0f;
}
