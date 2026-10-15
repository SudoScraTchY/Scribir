namespace Scriber.Pipeline;

/// <summary>
/// Execution stages of the end-to-end transcription pipeline.
/// </summary>
public enum PipelineStage
{
    NotStarted,
    Decoding,
    VoiceActivityDetection,
    Diarizing,
    Transcribing,
    Aligning,
    Normalizing,
    Completed,
    Failed
}
