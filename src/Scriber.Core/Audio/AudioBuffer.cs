namespace Scriber.Core.Audio;

/// <summary>
/// Raw PCM samples shared by every pipeline stage. Interleaved by channel when
/// <see cref="Channels"/> &gt; 1 (frame = Samples[i*Channels .. i*Channels+Channels-1]).
/// The canonical pipeline format is 16 kHz mono float32 in [-1.0, 1.0].
/// </summary>
public sealed record AudioBuffer
{
    /// <summary>Interleaved PCM float32 samples.</summary>
    public float[] Samples { get; init; }

    /// <summary>Sample rate in Hz.</summary>
    public int SampleRate { get; init; }

    /// <summary>Number of interleaved channels.</summary>
    public int Channels { get; init; }

    public AudioBuffer(float[] samples, int sampleRate, int channels)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0)
        {
            throw new ArgumentException("AudioBuffer must contain at least one sample.", nameof(samples));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentException("SampleRate must be positive.", nameof(sampleRate));
        }
        if (channels <= 0)
        {
            throw new ArgumentException("Channels must be positive.", nameof(channels));
        }
        if (samples.Length % channels != 0)
        {
            throw new ArgumentException("Samples length must be a whole number of frames.", nameof(samples));
        }

        Samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
    }

    public int FrameCount => Samples.Length / Channels;

    public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);
}
