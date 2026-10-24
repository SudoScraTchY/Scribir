using Scriber.Core.Domain;

namespace Scriber.Pipeline.Streaming;

/// <summary>
/// Reconciler contract for Two-Pass Flow B: reconciles live streaming draft transcripts
/// with offline high-accuracy transcripts, preserving any user edits made during recording.
/// </summary>
public interface ITwoPassReconciler
{
    Transcript Reconcile(Transcript liveDraft, Transcript offlineFinal);
}

/// <summary>
/// Default implementation of two-pass reconciliation.
/// Reconciles the high-accuracy offline transcript with any user edits made during the live phase.
/// Matches segments via temporal overlap (Intersection over Union).
/// </summary>
public sealed class TwoPassReconciler : ITwoPassReconciler
{
    private const double OverlapThreshold = 0.3; // At least 30% overlap to match segments

    public Transcript Reconcile(Transcript liveDraft, Transcript offlineFinal)
    {
        ArgumentNullException.ThrowIfNull(liveDraft);
        ArgumentNullException.ThrowIfNull(offlineFinal);

        // If the live draft has no segments, return the offline result directly
        if (liveDraft.Segments.Count == 0)
        {
            return offlineFinal;
        }

        // Detect if any live draft segments were edited by comparing or checking custom edits
        // If live draft has edits, merge them into the offline segments
        var liveSegments = liveDraft.Segments;
        var offlineSegments = offlineFinal.Segments.ToList();

        // Build mapping from offline segment to overlapping live segment
        var reconciledSegments = new List<Segment>(offlineSegments.Count);

        foreach (var offSeg in offlineSegments)
        {
            // Find best matching live segment by time overlap
            var matchingLive = FindBestMatch(offSeg, liveSegments);

            if (matchingLive != null && HasUserCorrection(matchingLive, offSeg))
            {
                // Preserve user corrected text and speaker attribution while keeping timestamps
                var words = SynthesizeWords(matchingLive.Text, offSeg.Start, offSeg.End);
                var reconciled = new Segment(matchingLive.SpeakerId, offSeg.Start, offSeg.End, words);
                reconciledSegments.Add(reconciled);
            }
            else
            {
                // Keep the offline segment
                reconciledSegments.Add(offSeg);
            }
        }

        // Return reconciled transcript preserving offline audio references and speaker palette
        return offlineFinal with
        {
            Title = !string.IsNullOrWhiteSpace(liveDraft.Title) ? liveDraft.Title : offlineFinal.Title,
            Segments = reconciledSegments
        };
    }

    private static Segment? FindBestMatch(Segment target, IReadOnlyList<Segment> candidates)
    {
        Segment? bestMatch = null;
        double bestIou = 0.0;

        foreach (var candidate in candidates)
        {
            double iou = ComputeTemporalIou(target.Start, target.End, candidate.Start, candidate.End);
            if (iou > bestIou && iou >= OverlapThreshold)
            {
                bestIou = iou;
                bestMatch = candidate;
            }
        }

        return bestMatch;
    }

    private static double ComputeTemporalIou(TimeSpan startA, TimeSpan endA, TimeSpan startB, TimeSpan endB)
    {
        var intersectionStart = startA > startB ? startA : startB;
        var intersectionEnd = endA < endB ? endA : endB;

        double intersection = Math.Max(0, (intersectionEnd - intersectionStart).TotalSeconds);
        if (intersection <= 0) return 0.0;

        var unionStart = startA < startB ? startA : startB;
        var unionEnd = endA > endB ? endA : endB;
        double union = (unionEnd - unionStart).TotalSeconds;

        return union > 0 ? intersection / union : 0.0;
    }

    private static bool HasUserCorrection(Segment live, Segment offline)
    {
        // If text was manually edited during live session (e.g. spelling correction), preserve it
        return !string.Equals(live.Text.Trim(), offline.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<Word> SynthesizeWords(string text, TimeSpan start, TimeSpan end)
    {
        var rawWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawWords.Length == 0) return [];

        double totalDuration = (end - start).TotalSeconds;
        double perWord = totalDuration / rawWords.Length;
        var words = new List<Word>(rawWords.Length);

        for (int i = 0; i < rawWords.Length; i++)
        {
            var wStart = start + TimeSpan.FromSeconds(i * perWord);
            var wEnd = (i == rawWords.Length - 1) ? end : start + TimeSpan.FromSeconds((i + 1) * perWord);
            if (wEnd <= wStart) wEnd = wStart + TimeSpan.FromMilliseconds(50);
            if (wEnd > end) wEnd = end;

            words.Add(new Word(rawWords[i], wStart, wEnd, 1.0));
        }

        return words;
    }
}
