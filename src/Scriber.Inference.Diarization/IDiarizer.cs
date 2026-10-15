using Scriber.Core.Audio;

namespace Scriber.Inference.Diarization;

/// <summary>
/// On-device speaker diarization abstraction for attributing audio ranges to speakers.
/// </summary>
public interface IDiarizer
{
    /// <summary>
    /// Diarizes the provided PCM audio buffer, returning sequential speaker turns.
    /// </summary>
    /// <param name="audio">Normalized 16 kHz mono float32 PCM audio.</param>
    /// <param name="options">Diarization options (operating mode, active threshold, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of speaker turns with stable, appearance-ordered speaker IDs.</returns>
    Task<IReadOnlyList<SpeakerTurn>> DiarizeAsync(AudioBuffer audio, DiarizationOptions options, CancellationToken ct = default);
}
