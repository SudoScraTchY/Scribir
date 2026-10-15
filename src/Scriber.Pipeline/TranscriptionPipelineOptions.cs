using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;
using Scriber.Pipeline.Normalization;
using Scriber.Pipeline.Vad;

namespace Scriber.Pipeline;

/// <summary>
/// Configuration options for executing an end-to-end transcription run.
/// </summary>
public record TranscriptionPipelineOptions
{
    /// <summary>
    /// Title of the transcript. Defaults to filename or "Untitled Transcript".
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// ISO language code (e.g., "en", "fa"). Defaults to "en".
    /// </summary>
    public string LanguageCode { get; init; } = "en";

    /// <summary>
    /// Whether to run Voice Activity Detection to inspect speech ranges. Defaults to false.
    /// </summary>
    public bool EnableVad { get; init; } = false;

    /// <summary>
    /// Options for VAD pass when enabled.
    /// </summary>
    public VadOptions VadOptions { get; init; } = new();

    /// <summary>
    /// Options for speaker diarization.
    /// </summary>
    public DiarizationOptions DiarizationOptions { get; init; } = new();

    /// <summary>
    /// Options for ASR recognition pass.
    /// </summary>
    public AsrOptions AsrOptions { get; init; } = new();

    /// <summary>
    /// Options for text normalization.
    /// </summary>
    public NormalizationOptions NormalizationOptions { get; init; } = new();
}
