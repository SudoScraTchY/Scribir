namespace Scriber.Core.Domain;

/// <summary>One recognized word with its alignment in the source audio.</summary>
public record Word
{
    /// <summary>The word text.</summary>
    public string Text { get; init; }

    /// <summary>Word start in the audio timeline.</summary>
    public TimeSpan Start { get; init; }

    /// <summary>Word end in the audio timeline.</summary>
    public TimeSpan End { get; init; }

    /// <summary>ASR confidence, 0.0 to 1.0 inclusive.</summary>
    public double Confidence { get; init; }

    public Word(string text, TimeSpan start, TimeSpan end, double confidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));
        if (end < start)
        {
            throw new ArgumentException("Word End must not be earlier than Start.", nameof(end));
        }
        if (confidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in [0.0, 1.0].");
        }

        Text = text;
        Start = start;
        End = end;
        Confidence = confidence;
    }
}
