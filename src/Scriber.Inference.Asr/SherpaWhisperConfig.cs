namespace Scriber.Inference.Asr;

/// <summary>
/// Configuration for the sherpa-onnx Whisper ASR backend.
/// </summary>
public sealed record SherpaWhisperConfig
{
    /// <summary>Path to Whisper encoder ONNX model.</summary>
    public required string EncoderPath { get; init; }

    /// <summary>Path to Whisper decoder ONNX model.</summary>
    public required string DecoderPath { get; init; }

    /// <summary>Path to Whisper tokens text file.</summary>
    public required string TokensPath { get; init; }

    /// <summary>Default target language code (e.g. "en", "fa").</summary>
    public string Language { get; init; } = "en";

    /// <summary>Whisper task: "transcribe" (default) or "translate".</summary>
    public string Task { get; init; } = "transcribe";

    /// <summary>Number of inference threads.</summary>
    public int NumThreads { get; init; } = Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>Execution provider: "cpu" (default).</summary>
    public string Provider { get; init; } = "cpu";
}
