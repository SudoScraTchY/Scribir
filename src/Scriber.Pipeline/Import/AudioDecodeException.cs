namespace Scriber.Pipeline.Import;

/// <summary>An audio file could not be decoded to PCM (corrupt, unsupported, or no audio stream).</summary>
public sealed class AudioDecodeException : Exception
{
    public AudioDecodeException()
    {
    }

    public AudioDecodeException(string message)
        : base(message)
    {
    }

    public AudioDecodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
