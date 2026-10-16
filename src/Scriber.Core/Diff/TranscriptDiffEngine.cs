namespace Scriber.Core.Diff;

using Scriber.Core.Domain;

public sealed class TranscriptDiffEngine
{
    public TranscriptDiffResult ComputeDiff(
        Transcript prior,
        Transcript fresh,
        ISet<int>? manuallyEditedSegmentIndices = null)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(fresh);

        var segmentDiffs = new List<SegmentDiff>();
        var conflicts = new List<ReconciliationConflict>();

        int count = Math.Max(prior.Segments.Count, fresh.Segments.Count);

        for (int i = 0; i < count; i++)
        {
            var oldSeg = i < prior.Segments.Count ? prior.Segments[i] : null;
            var newSeg = i < fresh.Segments.Count ? fresh.Segments[i] : null;

            if (oldSeg != null && newSeg != null)
            {
                bool speakerChanged = !string.Equals(oldSeg.SpeakerId, newSeg.SpeakerId, StringComparison.Ordinal);
                var wordDiffs = ComputeWordDiff(oldSeg.Words, newSeg.Words);

                var segDiff = new SegmentDiff(
                    i,
                    oldSeg.SpeakerId,
                    newSeg.SpeakerId,
                    speakerChanged,
                    oldSeg.Start,
                    oldSeg.End,
                    wordDiffs);

                segmentDiffs.Add(segDiff);

                string oldText = string.Join(" ", oldSeg.Words.Select(w => w.Text)).Trim();
                string newText = string.Join(" ", newSeg.Words.Select(w => w.Text)).Trim();

                bool isEdited = manuallyEditedSegmentIndices != null && manuallyEditedSegmentIndices.Contains(i);
                if (isEdited && !string.Equals(oldText, newText, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add(new ReconciliationConflict(
                        i,
                        oldText,
                        newText,
                        oldSeg.Start,
                        oldSeg.End,
                        ReconciliationDecision.Pending));
                }
            }
            else if (oldSeg != null)
            {
                // Deleted in fresh
                var wordDiffs = oldSeg.Words
                    .Select(w => new WordDiffItem(DiffOperation.Deleted, w.Text, null, w.Start, w.End))
                    .ToList();

                segmentDiffs.Add(new SegmentDiff(
                    i,
                    oldSeg.SpeakerId,
                    string.Empty,
                    true,
                    oldSeg.Start,
                    oldSeg.End,
                    wordDiffs));
            }
            else if (newSeg != null)
            {
                // Inserted in fresh
                var wordDiffs = newSeg.Words
                    .Select(w => new WordDiffItem(DiffOperation.Inserted, null, w.Text, w.Start, w.End))
                    .ToList();

                segmentDiffs.Add(new SegmentDiff(
                    i,
                    string.Empty,
                    newSeg.SpeakerId,
                    true,
                    newSeg.Start,
                    newSeg.End,
                    wordDiffs));
            }
        }

        int insertions = segmentDiffs.Sum(s => s.Words.Count(w => w.Operation == DiffOperation.Inserted));
        int deletions = segmentDiffs.Sum(s => s.Words.Count(w => w.Operation == DiffOperation.Deleted));
        int modifications = segmentDiffs.Sum(s => s.Words.Count(w => w.Operation == DiffOperation.Modified));

        return new TranscriptDiffResult(segmentDiffs, insertions, deletions, modifications, conflicts);
    }

    public static IReadOnlyList<WordDiffItem> ComputeWordDiff(IReadOnlyList<Word> oldWords, IReadOnlyList<Word> newWords)
    {
        int n = oldWords.Count;
        int m = newWords.Count;
        int[,] dp = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= m; j++)
            {
                if (i == 0 || j == 0)
                {
                    dp[i, j] = 0;
                }
                else if (string.Equals(oldWords[i - 1].Text, newWords[j - 1].Text, StringComparison.OrdinalIgnoreCase))
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }
        }

        var result = new List<WordDiffItem>();
        int curI = n, curJ = m;

        while (curI > 0 || curJ > 0)
        {
            if (curI > 0 && curJ > 0 && string.Equals(oldWords[curI - 1].Text, newWords[curJ - 1].Text, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new WordDiffItem(
                    DiffOperation.Unchanged,
                    oldWords[curI - 1].Text,
                    newWords[curJ - 1].Text,
                    newWords[curJ - 1].Start,
                    newWords[curJ - 1].End));
                curI--;
                curJ--;
            }
            else if (curJ > 0 && (curI == 0 || dp[curI, curJ - 1] >= dp[curI - 1, curJ]))
            {
                result.Add(new WordDiffItem(
                    DiffOperation.Inserted,
                    null,
                    newWords[curJ - 1].Text,
                    newWords[curJ - 1].Start,
                    newWords[curJ - 1].End));
                curJ--;
            }
            else if (curI > 0 && (curJ == 0 || dp[curI, curJ - 1] < dp[curI - 1, curJ]))
            {
                result.Add(new WordDiffItem(
                    DiffOperation.Deleted,
                    oldWords[curI - 1].Text,
                    null,
                    oldWords[curI - 1].Start,
                    oldWords[curI - 1].End));
                curI--;
            }
        }

        result.Reverse();
        return result;
    }
}

public sealed record TranscriptDiffResult(
    IReadOnlyList<SegmentDiff> SegmentDiffs,
    int InsertionsCount,
    int DeletionsCount,
    int ModificationsCount,
    IReadOnlyList<ReconciliationConflict> ManualConflicts
);
