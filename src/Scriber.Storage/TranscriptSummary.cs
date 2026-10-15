namespace Scriber.Storage;

/// <summary>
/// Lightweight metadata summary of a stored transcript for library list views.
/// </summary>
public record TranscriptSummary(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    string LanguageCode,
    TimeSpan Duration,
    int SpeakerCount,
    int SegmentCount);
