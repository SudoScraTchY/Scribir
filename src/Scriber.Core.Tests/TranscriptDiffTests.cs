namespace Scriber.Core.Tests;

using Scriber.Core.Diff;
using Scriber.Core.Domain;
using Xunit;

public class TranscriptDiffTests
{
    private static Transcript CreateSampleTranscript(Guid id, string speakerId, string text)
    {
        var audio = new AudioReference("sample.wav", TimeSpan.FromSeconds(30), 16000);
        var speaker = new Speaker(speakerId, "Speaker A", "#3B82F6");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((w, i) => new Word(w, TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 0.9), 0.95f))
            .ToList();
        var segment = new Segment(speakerId, TimeSpan.Zero, TimeSpan.FromSeconds(words.Count), words);

        return new Transcript(id, "Test Meeting", DateTimeOffset.UtcNow, "en", [speaker], [segment], audio);
    }

    [Fact]
    public void WordDiff_IdenticalWords_AllUnchanged()
    {
        var oldWords = new List<Word>
        {
            new("Hello", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.5), 0.9f),
            new("world", TimeSpan.FromSeconds(0.6), TimeSpan.FromSeconds(1.0), 0.9f)
        };
        var newWords = new List<Word>
        {
            new("Hello", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.5), 0.99f),
            new("world", TimeSpan.FromSeconds(0.6), TimeSpan.FromSeconds(1.0), 0.99f)
        };

        var diff = TranscriptDiffEngine.ComputeWordDiff(oldWords, newWords);

        Assert.Equal(2, diff.Count);
        Assert.All(diff, d => Assert.Equal(DiffOperation.Unchanged, d.Operation));
    }

    [Fact]
    public void WordDiff_DetectsInsertionsAndDeletions()
    {
        var oldWords = new List<Word>
        {
            new("The", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.4), 0.9f),
            new("fast", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.9), 0.9f),
            new("fox", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.4), 0.9f)
        };
        var newWords = new List<Word>
        {
            new("The", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.4), 0.99f),
            new("quick", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.8), 0.99f),
            new("brown", TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(1.2), 0.99f),
            new("fox", TimeSpan.FromSeconds(1.3), TimeSpan.FromSeconds(1.6), 0.99f)
        };

        var diff = TranscriptDiffEngine.ComputeWordDiff(oldWords, newWords);

        Assert.Contains(diff, d => d.Operation == DiffOperation.Deleted && d.OldWord == "fast");
        Assert.Contains(diff, d => d.Operation == DiffOperation.Inserted && d.NewWord == "quick");
        Assert.Contains(diff, d => d.Operation == DiffOperation.Inserted && d.NewWord == "brown");
        Assert.Contains(diff, d => d.Operation == DiffOperation.Unchanged && d.OldWord == "fox");
    }

    [Fact]
    public void ComputeDiff_DetectsSpeakerChangeAndManualEditConflict()
    {
        var id = Guid.NewGuid();
        var prior = CreateSampleTranscript(id, "spk_0", "Manual user corrected sentence here.");
        var fresh = CreateSampleTranscript(id, "spk_1", "Model recognized words here.");

        var engine = new TranscriptDiffEngine();
        var diff = engine.ComputeDiff(prior, fresh, manuallyEditedSegmentIndices: new HashSet<int> { 0 });

        Assert.Single(diff.SegmentDiffs);
        Assert.True(diff.SegmentDiffs[0].SpeakerChanged);
        Assert.Equal("spk_0", diff.SegmentDiffs[0].OldSpeakerId);
        Assert.Equal("spk_1", diff.SegmentDiffs[0].NewSpeakerId);

        // Conflict was detected and flagged for review
        Assert.Single(diff.ManualConflicts);
        Assert.Equal(0, diff.ManualConflicts[0].SegmentIndex);
        Assert.Contains("Manual user corrected", diff.ManualConflicts[0].OriginalEditedText);
        Assert.Contains("Model recognized", diff.ManualConflicts[0].NewModelText);
    }

    [Fact]
    public void TranscriptMergeResolver_KeepEdited_PreservesManualCorrection()
    {
        var id = Guid.NewGuid();
        var prior = CreateSampleTranscript(id, "spk_0", "Important custom correction.");
        var fresh = CreateSampleTranscript(id, "spk_0", "Generic speech recognition output.");

        var conflict = new ReconciliationConflict(
            0,
            "Important custom correction.",
            "Generic speech recognition output.",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3),
            ReconciliationDecision.KeepEdited);

        var merged = TranscriptMergeResolver.Resolve(prior, fresh, [conflict]);

        string text = string.Join(" ", merged.Segments[0].Words.Select(w => w.Text));
        Assert.Equal("Important custom correction.", text);
    }

    [Fact]
    public void TranscriptMergeResolver_UseNew_AdoptsModelReTranscription()
    {
        var id = Guid.NewGuid();
        var prior = CreateSampleTranscript(id, "spk_0", "Old prior text.");
        var fresh = CreateSampleTranscript(id, "spk_0", "Higher accuracy upgrade.");

        var conflict = new ReconciliationConflict(
            0,
            "Old prior text.",
            "Higher accuracy upgrade.",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3),
            ReconciliationDecision.UseNew);

        var merged = TranscriptMergeResolver.Resolve(prior, fresh, [conflict]);

        string text = string.Join(" ", merged.Segments[0].Words.Select(w => w.Text));
        Assert.Equal("Higher accuracy upgrade.", text);
    }
}
