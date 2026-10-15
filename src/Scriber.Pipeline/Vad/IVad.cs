using Scriber.Core.Audio;

namespace Scriber.Pipeline.Vad;

/// <summary>Voice-activity detection over a normalized PCM buffer.</summary>
public interface IVad
{
    /// <summary>Returns the speech ranges of <paramref name="audio"/>, silence excluded.</summary>
    IReadOnlyList<SpeechRange> DetectSpeech(AudioBuffer audio, CancellationToken ct = default);
}
