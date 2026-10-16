namespace Scriber.Inference.SpeakerId;

using Scriber.Core.Domain;
using Scriber.Core.Inference;

/// <summary>
/// Compares diarized speaker embeddings against enrolled local voiceprints (ADR-0010).
/// Generates non-silent match suggestions with confidence scores.
/// </summary>
public sealed class SpeakerMatcher
{
    public float DefaultThreshold { get; }

    public SpeakerMatcher(float defaultThreshold = 0.60f)
    {
        if (defaultThreshold is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultThreshold), "Threshold must be between 0.0 and 1.0.");
        }

        DefaultThreshold = defaultThreshold;
    }

    /// <summary>
    /// Evaluates a speaker embedding against a collection of enrolled voiceprints.
    /// Returns the best candidate and indicates whether it meets the similarity threshold.
    /// </summary>
    public SpeakerMatchResult Match(
        string speakerId,
        ReadOnlySpan<float> segmentEmbedding,
        IReadOnlyList<Voiceprint> enrolledVoiceprints,
        float? threshold = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speakerId);
        float minSimilarity = threshold ?? DefaultThreshold;

        if (enrolledVoiceprints == null || enrolledVoiceprints.Count == 0 || segmentEmbedding.Length == 0)
        {
            return new SpeakerMatchResult(speakerId, null, false);
        }

        SpeakerMatchCandidate? bestCandidate = null;

        foreach (var vp in enrolledVoiceprints)
        {
            float sim = SpeakerEmbeddingMath.CosineSimilarity(segmentEmbedding, vp.Embedding.Span);
            if (bestCandidate == null || sim > bestCandidate.Similarity)
            {
                bestCandidate = new SpeakerMatchCandidate(vp, sim);
            }
        }

        bool meets = bestCandidate != null && bestCandidate.Similarity >= minSimilarity;
        return new SpeakerMatchResult(speakerId, bestCandidate, meets);
    }
}
