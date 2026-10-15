namespace Scriber.Storage;

/// <summary>
/// Full-text search result linking to a specific segment within a transcript.
/// </summary>
public record SearchResult(
    Guid TranscriptId,
    string TranscriptTitle,
    long SegmentId,
    string SpeakerId,
    TimeSpan Start,
    TimeSpan End,
    string Text,
    string Snippet);
