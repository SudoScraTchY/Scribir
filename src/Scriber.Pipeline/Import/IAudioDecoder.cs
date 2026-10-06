using Scriber.Core.Audio;

namespace Scriber.Pipeline.Import;

/// <summary>
/// Decodes an audio/video container into the pipeline's canonical PCM format
/// (16 kHz mono float32, see <see cref="AudioBuffer"/>).
/// </summary>
public interface IAudioDecoder
{
    /// <summary>Decodes the file at <paramref name="filePath"/> to normalized PCM.</summary>
    /// <exception cref="AudioDecodeException">The file is corrupt, unsupported, or has no audio stream.</exception>
    Task<AudioBuffer> DecodeAsync(string filePath, CancellationToken ct = default);
}
