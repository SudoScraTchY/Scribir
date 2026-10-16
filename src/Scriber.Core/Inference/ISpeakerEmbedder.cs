namespace Scriber.Core.Inference;

using Scriber.Core.Audio;

/// <summary>
/// Extracts fixed-dimension speaker embedding vectors from audio segments (ADR-0010).
/// </summary>
public interface ISpeakerEmbedder : IDisposable
{
    /// <summary>Dimensionality of the embedding vector (e.g. 192, 256, 512).</summary>
    int EmbeddingDimension { get; }

    /// <summary>
    /// Computes a speaker embedding vector for the provided audio segment.
    /// Audio must be 16kHz mono.
    /// </summary>
    Task<float[]> ExtractEmbeddingAsync(AudioBuffer segment, CancellationToken ct = default);
}
