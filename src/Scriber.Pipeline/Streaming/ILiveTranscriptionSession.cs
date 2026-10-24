using Scriber.Core.Domain;

namespace Scriber.Pipeline.Streaming;

/// <summary>
/// A real-time interim or finalized segment update produced during live streaming.
/// </summary>
public record LiveSegmentUpdate(
    string SpeakerId,
    string Text,
    TimeSpan Start,
    TimeSpan End,
    bool IsFinal);

/// <summary>
/// Coordinates Flow B real-time streaming audio ingestion, partial ASR/diarization, and pause/resume lifecycle.
/// </summary>
public interface ILiveTranscriptionSession : IAsyncDisposable
{
    event EventHandler<LiveSegmentUpdate>? SegmentUpdated;
    IReadOnlyList<LiveSegmentUpdate> Segments { get; }
    bool IsPaused { get; }

    ValueTask PushAudioAsync(ReadOnlyMemory<float> samples, TimeSpan timestamp, CancellationToken ct = default);
    void Pause();
    void Resume();
    ValueTask<Transcript> CompleteDraftAsync(string sourcePath, string? title = null, CancellationToken ct = default);
}
