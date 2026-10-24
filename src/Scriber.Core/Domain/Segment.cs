namespace Scriber.Core.Domain;

/// <summary>A contiguous, single-speaker stretch of transcript.</summary>
public record Segment
{
    /// <summary>Must reference a <see cref="Speaker.Id"/> of the owning transcript.</summary>
    public string SpeakerId { get; init; }

    /// <summary>Segment start in the audio timeline.</summary>
    public TimeSpan Start { get; init; }

    /// <summary>Segment end; strictly later than <see cref="Start"/>.</summary>
    public TimeSpan End { get; init; }

    /// <summary>Words in chronological order, all inside [Start, End].</summary>
    public IReadOnlyList<Word> Words { get; init; }

    /// <summary>Convenience aggregated text of all words in this segment.</summary>
    public string Text => string.Join(" ", Words.Select(w => w.Text));

    public Segment(string speakerId, TimeSpan start, TimeSpan end, IReadOnlyList<Word> words)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speakerId, nameof(speakerId));
        if (end <= start)
        {
            throw new ArgumentException("Segment End must be later than Start.", nameof(end));
        }
        ArgumentNullException.ThrowIfNull(words);

        Word? previous = null;
        foreach (var word in words)
        {
            ArgumentNullException.ThrowIfNull(word, nameof(words));
            if (word.Start < start || word.End > end)
            {
                throw new ArgumentException(
                    $"Word '{word.Text}' [{word.Start}–{word.End}] falls outside its segment [{start}–{end}].");
            }

            if (previous is not null && word.Start < previous.Start)
            {
                throw new ArgumentException("Segment words must be in chronological order.");
            }

            previous = word;
        }

        SpeakerId = speakerId;
        Start = start;
        End = end;
        Words = words;
    }
}
