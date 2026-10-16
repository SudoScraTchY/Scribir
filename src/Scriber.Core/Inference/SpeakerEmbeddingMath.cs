namespace Scriber.Core.Inference;

/// <summary>
/// Mathematical utilities for vector similarity and distance calculations on speaker embeddings.
/// </summary>
public static class SpeakerEmbeddingMath
{
    /// <summary>
    /// Computes cosine similarity between two embedding vectors: (a · b) / (||a|| * ||b||).
    /// Returns a value in [-1.0, 1.0], or 0 if empty or length mismatch.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return 0f;
        }

        float dot = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            float va = a[i];
            float vb = b[i];
            dot += va * vb;
            normA += va * va;
            normB += vb * vb;
        }

        if (normA <= 0f || normB <= 0f)
        {
            return 0f;
        }

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
