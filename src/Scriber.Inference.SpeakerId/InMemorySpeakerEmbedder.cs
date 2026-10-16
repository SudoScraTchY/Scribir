namespace Scriber.Inference.SpeakerId;

using Scriber.Core.Audio;
using Scriber.Core.Inference;

/// <summary>
/// In-memory deterministic speaker embedder useful for testing without downloading full ONNX weights.
/// Computes a normalized energy summary vector from the audio waveform.
/// </summary>
public sealed class InMemorySpeakerEmbedder : ISpeakerEmbedder
{
    public int EmbeddingDimension { get; }

    public InMemorySpeakerEmbedder(int dimension = 192)
    {
        if (dimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be positive.");
        }

        EmbeddingDimension = dimension;
    }

    public Task<float[]> ExtractEmbeddingAsync(AudioBuffer segment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (segment.Samples.Length == 0)
        {
            throw new ArgumentException("Audio segment cannot be empty.", nameof(segment));
        }

        ct.ThrowIfCancellationRequested();

        var embedding = new float[EmbeddingDimension];
        var samples = segment.Samples;
        int step = Math.Max(1, samples.Length / EmbeddingDimension);

        for (int i = 0; i < EmbeddingDimension; i++)
        {
            float sum = 0f;
            int count = 0;
            int start = i * step;
            int end = Math.Min(samples.Length, start + step);
            for (int j = start; j < end; j++)
            {
                sum += MathF.Abs(samples[j]);
                count++;
            }

            embedding[i] = count > 0 ? (sum / count) : 0f;
        }

        // L2-normalize
        float norm = 0f;
        for (int i = 0; i < embedding.Length; i++)
        {
            norm += embedding[i] * embedding[i];
        }

        norm = MathF.Sqrt(norm);
        if (norm > 0f)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= norm;
            }
        }

        return Task.FromResult(embedding);
    }

    public void Dispose()
    {
        // No-op for in-memory embedder
    }
}
