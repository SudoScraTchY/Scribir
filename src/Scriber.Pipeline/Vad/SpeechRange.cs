namespace Scriber.Pipeline.Vad;

/// <summary>A contiguous span of detected speech in the source audio.</summary>
/// <param name="Start">Speech start.</param>
/// <param name="End">Speech end (exclusive).</param>
public sealed record SpeechRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}
