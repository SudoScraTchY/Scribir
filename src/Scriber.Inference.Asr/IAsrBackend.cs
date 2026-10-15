using Scriber.Core.Audio;

namespace Scriber.Inference.Asr;

/// <summary>
/// Pluggable automated speech recognition (ASR) backend interface per ADR-0005.
/// </summary>
public interface IAsrBackend
{
    /// <summary>Unique machine-readable backend identifier (e.g. "sherpa-whisper-small-int8").</summary>
    string Id { get; }

    /// <summary>Human-readable display name for UI presentation.</summary>
    string DisplayName { get; }

    /// <summary>List of ISO language codes supported by this backend.</summary>
    IReadOnlyList<string> SupportedLanguageCodes { get; }

    /// <summary>Whether this backend supports live streaming partial transcription.</summary>
    bool SupportsStreaming { get; }

    /// <summary>
    /// Transcribes the normalized PCM audio buffer, returning word-attributed transcript segments.
    /// </summary>
    Task<TranscriptSegment[]> TranscribeAsync(AudioBuffer audio, AsrOptions options, CancellationToken ct = default);

    /// <summary>
    /// Live streaming transcription consuming audio chunks and yielding interim/final transcript segments.
    /// </summary>
    IAsyncEnumerable<PartialTranscriptSegment> TranscribeStreamingAsync(IAsyncEnumerable<AudioChunk> chunks, AsrOptions options, CancellationToken ct = default);
}
