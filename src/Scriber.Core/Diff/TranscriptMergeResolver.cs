namespace Scriber.Core.Diff;

using Scriber.Core.Domain;

public static class TranscriptMergeResolver
{
    public static Transcript Resolve(
        Transcript prior,
        Transcript fresh,
        IEnumerable<ReconciliationConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentNullException.ThrowIfNull(conflicts);

        var conflictMap = conflicts.ToDictionary(c => c.SegmentIndex, c => c);
        var mergedSegments = new List<Segment>();

        int count = Math.Max(prior.Segments.Count, fresh.Segments.Count);

        for (int i = 0; i < count; i++)
        {
            var oldSeg = i < prior.Segments.Count ? prior.Segments[i] : null;
            var newSeg = i < fresh.Segments.Count ? fresh.Segments[i] : null;

            if (conflictMap.TryGetValue(i, out var conflict) && conflict.Decision == ReconciliationDecision.KeepEdited && oldSeg != null)
            {
                // User explicitly chose to keep prior manual correction
                mergedSegments.Add(oldSeg);
            }
            else if (newSeg != null)
            {
                // Adopt fresh re-transcription
                mergedSegments.Add(newSeg);
            }
            else if (oldSeg != null)
            {
                mergedSegments.Add(oldSeg);
            }
        }

        return new Transcript(
            prior.Id,
            fresh.Title,
            DateTimeOffset.UtcNow,
            fresh.LanguageCode,
            fresh.Speakers,
            mergedSegments,
            fresh.Audio);
    }
}
