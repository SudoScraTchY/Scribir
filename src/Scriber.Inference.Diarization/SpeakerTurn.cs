namespace Scriber.Inference.Diarization;

/// <summary>
/// A time span during which a single identified speaker was actively speaking.
/// </summary>
/// <param name="SpeakerId">Stable speaker identifier (e.g. "speaker-0", "speaker-1").</param>
/// <param name="Start">Start timestamp relative to the audio buffer origin.</param>
/// <param name="End">End timestamp relative to the audio buffer origin.</param>
public record SpeakerTurn(string SpeakerId, TimeSpan Start, TimeSpan End)
{
    /// <summary>Duration of the speaker turn.</summary>
    public TimeSpan Duration => End - Start;
}
