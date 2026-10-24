using System.Numerics.Tensors;

namespace Scriber.Core.Inference;

/// <summary>
/// Mathematical utilities for vector similarity and distance calculations on speaker embeddings.
/// Optimized using SIMD and TensorPrimitives hardware acceleration (ARM NEON / AVX2).
/// </summary>
public static class SpeakerEmbeddingMath
{
    /// <summary>
    /// Computes cosine similarity between two embedding vectors: (a · b) / (||a|| * ||b||).
    /// Returns a value in [-1.0, 1.0], or 0 if empty or length mismatch.
    /// Hardware-accelerated with SIMD / TensorPrimitives.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return 0f;
        }

        float sim = TensorPrimitives.CosineSimilarity(a, b);
        if (float.IsNaN(sim) || float.IsInfinity(sim))
        {
            return 0f;
        }

        return Math.Clamp(sim, -1f, 1f);
    }

    /// <summary>
    /// Computes the element-wise average of two embedding vectors and applies L2 normalization:
    /// dest = normalize((a + b) / 2).
    /// Vectorized using TensorPrimitives hardware acceleration (ARM NEON / AVX2).
    /// </summary>
    public static void AverageAndNormalize(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> destination)
    {
        if (a.Length != b.Length || destination.Length < a.Length)
        {
            throw new ArgumentException("Vectors must have matching dimensions.");
        }

        TensorPrimitives.Add(a, b, destination);
        TensorPrimitives.Multiply(destination, 0.5f, destination);
        float norm = TensorPrimitives.Norm(destination);
        if (norm > 0f)
        {
            TensorPrimitives.Divide(destination, norm, destination);
        }
    }
}
