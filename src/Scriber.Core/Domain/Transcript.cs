namespace Scriber.Core.Domain;

/// <summary>
/// The top-level product artifact: a speaker-attributed transcript of one audio file.
/// Shape is load-bearing — every Phase 1+ task consumes this.
/// </summary>
public record Transcript
{
    public Guid Id { get; init; }
    public string Title { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string LanguageCode { get; init; }
    public IReadOnlyList<Speaker> Speakers { get; init; }
    public IReadOnlyList<Segment> Segments { get; init; }
    public AudioReference Audio { get; init; }

    public Transcript(
        Guid id,
        string title,
        DateTimeOffset createdAt,
        string languageCode,
        IReadOnlyList<Speaker> speakers,
        IReadOnlyList<Segment> segments,
        AudioReference audio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode, nameof(languageCode));
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(audio);

        // Every segment must resolve to a speaker declared on the transcript.
        var speakerIds = new HashSet<string>(
            speakers.Select(s => { ArgumentNullException.ThrowIfNull(s, nameof(speakers)); return s.Id; }),
            StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            ArgumentNullException.ThrowIfNull(segment, nameof(segments));
            if (!speakerIds.Contains(segment.SpeakerId))
            {
                throw new ArgumentException(
                    $"Segment [{segment.Start}–{segment.End}] references unknown speaker '{segment.SpeakerId}'.");
            }
        }

        Id = id;
        Title = title;
        CreatedAt = createdAt;
        LanguageCode = languageCode;
        Speakers = speakers;
        Segments = segments;
        Audio = audio;
    }
}
