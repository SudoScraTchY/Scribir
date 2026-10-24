using Scriber.Core.Audio;

namespace Scriber.Inference.Diarization;

/// <summary>
/// Lightweight fallback diarizer for single-speaker recordings, quick transcription without diarization,
/// or when multi-speaker diarization neural network weights have not been downloaded.
/// Treats the entire audio timeline as a single speaker turn (speaker-0).
/// </summary>
public sealed class PassthroughDiarizer : IDiarizer
{
    public Task<IReadOnlyList<SpeakerTurn>> DiarizeAsync(AudioBuffer audio, DiarizationOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<SpeakerTurn> turns =
        [
            new SpeakerTurn("speaker-0", TimeSpan.Zero, audio.Duration)
        ];

        return Task.FromResult(turns);
    }
}
