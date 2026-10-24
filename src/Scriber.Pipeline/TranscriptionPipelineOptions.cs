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
    /// ISO language code (e.g., "en", "fa", "auto"). Defaults to "auto".
    /// </summary>
    public string LanguageCode { get; init; } = "auto";

    /// <summary>
    /// Selected model identifier from the model catalog. Defaults to "sherpa-whisper-small-int8".
    /// </summary>
    public string ModelId { get; init; } = "sherpa-whisper-small-int8";

    /// <summary>
    /// Whether to preserve the raw audio file session for re-transcription with other models and voiceprint reference.
    /// </summary>
    public bool SaveRawAudio { get; init; } = true;

    /// <summary>
    /// Whether to run Voice Activity Detection to inspect speech ranges. Defaults to true.
    /// </summary>
    public bool EnableVad { get; init; } = true;

    /// <summary>
    /// Whether to run multi-speaker diarization to attribute turns. If false or model absent, falls back to single-speaker pass.
    /// </summary>
    public bool EnableDiarization { get; init; } = true;

    /// <summary>
    /// Whether to perform cross-session speaker voiceprint matching and enrollment. Defaults to true.
    /// </summary>
    public bool EnableVoiceprints { get; init; } = true;

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
