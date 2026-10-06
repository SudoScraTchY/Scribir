namespace Scriber.Core.Domain;

/// <summary>Reference to the source audio a transcript was produced from.</summary>
public record AudioReference
{
    /// <summary>Local file path of the source audio.</summary>
    public string LocalPath { get; init; }

    /// <summary>Total source audio duration, strictly positive.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Source sample rate in Hz, strictly positive.</summary>
    public int SampleRate { get; init; }

    public AudioReference(string localPath, TimeSpan duration, int sampleRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath, nameof(localPath));
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Duration must be positive.", nameof(duration));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentException("SampleRate must be positive.", nameof(sampleRate));
        }

        LocalPath = localPath;
        Duration = duration;
        SampleRate = sampleRate;
    }
}
