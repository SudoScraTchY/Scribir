using Scriber.Core.Domain;

namespace Scriber.Inference.Asr;

/// <summary>
/// A section of transcribed speech containing text, start/end timestamps, and word-level timing/confidence.
/// </summary>
/// <param name="Text">Full text of the transcribed segment.</param>
/// <param name="Start">Start timestamp relative to the audio buffer origin.</param>
/// <param name="End">End timestamp relative to the audio buffer origin.</param>
/// <param name="Words">Word-level tokens with timestamps and confidence scores.</param>
public record TranscriptSegment(string Text, TimeSpan Start, TimeSpan End, IReadOnlyList<Word> Words)
{
    /// <summary>Duration of the transcript segment.</summary>
    public TimeSpan Duration => End - Start;
}
