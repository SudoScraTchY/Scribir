namespace Scriber.Inference.Asr;

/// <summary>
/// A real-time partial or interim transcription segment emitted during live streaming.
/// </summary>
/// <param name="Text">Partial or final transcribed text.</param>
/// <param name="Start">Start timestamp relative to stream origin.</param>
/// <param name="End">End timestamp relative to stream origin.</param>
/// <param name="IsFinal">True if this segment is finalized and will not change.</param>
public record PartialTranscriptSegment(string Text, TimeSpan Start, TimeSpan End, bool IsFinal = false);
