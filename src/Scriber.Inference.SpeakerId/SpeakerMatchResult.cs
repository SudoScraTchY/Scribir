namespace Scriber.Inference.SpeakerId;

using Scriber.Core.Domain;

/// <summary>
/// A candidate enrolled voiceprint match with computed cosine similarity score (ADR-0010).
/// </summary>
public sealed record SpeakerMatchCandidate(Voiceprint Voiceprint, float Similarity);

/// <summary>
/// Match result for a diarized speaker compared against enrolled voiceprints.
/// </summary>
public sealed record SpeakerMatchResult
{
    public string DiarizedSpeakerId { get; init; }
    public SpeakerMatchCandidate? BestMatch { get; init; }
    public bool MeetsThreshold { get; init; }

    public SpeakerMatchResult(string diarizedSpeakerId, SpeakerMatchCandidate? bestMatch, bool meetsThreshold)
    {
        DiarizedSpeakerId = diarizedSpeakerId;
        BestMatch = bestMatch;
        MeetsThreshold = meetsThreshold;
    }
}
