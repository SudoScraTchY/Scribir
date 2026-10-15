using Scriber.Core.Domain;
using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;
using Scriber.Pipeline.Alignment;
using Xunit;

namespace Scriber.Pipeline.Tests;

public class AlignmentTests
{
    private static Word W(string text, double startSec, double endSec, double conf = 0.9) =>
        new(text, TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec), conf);

    private static SpeakerTurn T(string speakerId, double startSec, double endSec) =>
        new(speakerId, TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec));

    [Fact]
    public void Align_EmptyWords_ReturnsEmptyList()
    {
        var aligner = new TranscriptAligner();
        var turns = new[] { T("speaker-0", 0.0, 5.0) };

        var segments = aligner.Align(turns, Array.Empty<Word>());

        Assert.Empty(segments);
    }

    [Fact]
    public void Align_NoTurns_AttributesToDefaultSpeaker()
    {
        var aligner = new TranscriptAligner();
        var words = new[]
        {
            W("hello", 1.0, 1.5),
            W("world", 1.6, 2.0)
        };

        var segments = aligner.Align(Array.Empty<SpeakerTurn>(), words);

        Assert.Single(segments);
        Assert.Equal("speaker-0", segments[0].SpeakerId);
        Assert.Equal(2, segments[0].Words.Count);
        Assert.Equal("hello", segments[0].Words[0].Text);
    }

    [Fact]
    public void Align_SimpleNoBoundaryOverlap_AttributesCorrectSpeakers()
    {
        // Simple case: Speaker 0 speaks 0..5s, Speaker 1 speaks 6..10s
        var aligner = new TranscriptAligner();
        var turns = new[]
        {
            T("speaker-0", 0.0, 5.0),
            T("speaker-1", 6.0, 10.0)
        };

        var words = new[]
        {
            W("first", 1.0, 1.8),
            W("speaker", 2.0, 2.5),
            W("second", 6.5, 7.2),
            W("person", 7.5, 8.1)
        };

        var segments = aligner.Align(turns, words);

        Assert.Equal(2, segments.Count);

        // Turn 1
        Assert.Equal("speaker-0", segments[0].SpeakerId);
        Assert.Equal(2, segments[0].Words.Count);
        Assert.Equal("first", segments[0].Words[0].Text);
        Assert.Equal("speaker", segments[0].Words[1].Text);
        Assert.True(segments[0].Start <= words[0].Start);
        Assert.True(segments[0].End >= words[1].End);

        // Turn 2
        Assert.Equal("speaker-1", segments[1].SpeakerId);
        Assert.Equal(2, segments[1].Words.Count);
        Assert.Equal("second", segments[1].Words[0].Text);
        Assert.Equal("person", segments[1].Words[1].Text);
        Assert.True(segments[1].Start <= words[2].Start);
        Assert.True(segments[1].End >= words[3].End);
    }

    [Fact]
    public void Align_WordCrossingBoundary_MidpointSplitAssignsToMajorityOverlap()
    {
        // Turn A ends at 5.0s, Turn B starts at 5.0s.
        // Boundary = 5.0s.
        var aligner = new TranscriptAligner();
        var turns = new[]
        {
            T("speaker-A", 0.0, 5.0),
            T("speaker-B", 5.0, 10.0)
        };

        // Word 1: 4.6 to 5.2s.
        // Duration = 0.6s.
        // Overlap with A [4.6, 5.0] = 0.4s.
        // Overlap with B [5.0, 5.2] = 0.2s.
        // Midpoint = 4.9s (< 5.0s) -> Majority in Turn A -> Attributed to speaker-A.
        var wordBefore = W("leaning-left", 4.6, 5.2);

        // Word 2: 4.9 to 5.5s.
        // Duration = 0.6s.
        // Overlap with A [4.9, 5.0] = 0.1s.
        // Overlap with B [5.0, 5.5] = 0.5s.
        // Midpoint = 5.2s (> 5.0s) -> Majority in Turn B -> Attributed to speaker-B.
        var wordAfter = W("leaning-right", 4.9, 5.5);

        var segments = aligner.Align(turns, new[] { wordBefore, wordAfter });

        Assert.Equal(2, segments.Count);
        Assert.Equal("speaker-A", segments[0].SpeakerId);
        Assert.Single(segments[0].Words);
        Assert.Equal("leaning-left", segments[0].Words[0].Text);

        Assert.Equal("speaker-B", segments[1].SpeakerId);
        Assert.Single(segments[1].Words);
        Assert.Equal("leaning-right", segments[1].Words[0].Text);
    }

    [Fact]
    public void Align_OverlappingSpeech_AssignsToDominantFirstTurn()
    {
        // Turn 1 starts at 2.0s and ends at 6.0s (Speaker 1).
        // Turn 2 starts at 3.0s and ends at 7.0s (Speaker 2).
        // They overlap in [3.0s, 6.0s].
        var aligner = new TranscriptAligner();
        var turns = new[]
        {
            T("speaker-1", 2.0, 6.0),
            T("speaker-2", 3.0, 7.0)
        };

        // Word A: 2.2 to 2.8s (only Speaker 1)
        var word1 = W("solo1", 2.2, 2.8);

        // Word B: 3.5 to 4.5s (in the overlap [3.0, 6.0], 1.0s overlap with both!).
        // Per v1 rule: exact tie is broken in favor of dominant/first turn (speaker-1).
        var wordOverlapTie = W("both", 3.5, 4.5);

        // Word C: 5.5 to 6.5s (0.5s overlap with speaker-1, 1.0s overlap with speaker-2).
        // Overlap with speaker-2 is greater -> speaker-2.
        var word2 = W("solo2", 5.5, 6.5);

        var segments = aligner.Align(turns, new[] { word1, wordOverlapTie, word2 });

        Assert.Equal(2, segments.Count);

        // Segment 1 (speaker-1): contains word1 and wordOverlapTie
        Assert.Equal("speaker-1", segments[0].SpeakerId);
        Assert.Equal(2, segments[0].Words.Count);
        Assert.Equal("solo1", segments[0].Words[0].Text);
        Assert.Equal("both", segments[0].Words[1].Text);

        // Segment 2 (speaker-2): contains word2
        Assert.Equal("speaker-2", segments[1].SpeakerId);
        Assert.Single(segments[1].Words);
        Assert.Equal("solo2", segments[1].Words[0].Text);
    }

    [Fact]
    public void Align_WordInSilenceGap_AttributesToNearestTurn()
    {
        // Turn 1 is 1.0..3.0s. Silence is 3.0..7.0s. Turn 2 is 7.0..9.0s.
        var aligner = new TranscriptAligner();
        var turns = new[]
        {
            T("speaker-1", 1.0, 3.0),
            T("speaker-2", 7.0, 9.0)
        };

        // Word at 3.2..3.6s (closer to Turn 1)
        var wordNear1 = W("near-one", 3.2, 3.6);

        // Word at 6.2..6.6s (closer to Turn 2)
        var wordNear2 = W("near-two", 6.2, 6.6);

        var segments = aligner.Align(turns, new[] { wordNear1, wordNear2 });

        Assert.Equal(2, segments.Count);
        Assert.Equal("speaker-1", segments[0].SpeakerId);
        Assert.Equal("near-one", segments[0].Words[0].Text);

        Assert.Equal("speaker-2", segments[1].SpeakerId);
        Assert.Equal("near-two", segments[1].Words[0].Text);
    }

    [Fact]
    public void Align_WithAsrSegments_ProducesValidDomainSegments()
    {
        var aligner = new TranscriptAligner();
        var turns = new[]
        {
            T("spk-0", 0.0, 4.0),
            T("spk-1", 5.0, 9.0)
        };

        var asrSegments = new[]
        {
            new TranscriptSegment("Welcome everyone", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(3.5),
            [
                W("Welcome", 0.5, 1.5),
                W("everyone", 1.8, 3.5)
            ]),
            new TranscriptSegment("Thank you", TimeSpan.FromSeconds(5.2), TimeSpan.FromSeconds(7.0),
            [
                W("Thank", 5.2, 6.0),
                W("you", 6.1, 7.0)
            ])
        };

        var result = aligner.Align(turns, asrSegments);

        Assert.Equal(2, result.Count);
        Assert.Equal("spk-0", result[0].SpeakerId);
        Assert.Equal(2, result[0].Words.Count);
        Assert.Equal("spk-1", result[1].SpeakerId);
        Assert.Equal(2, result[1].Words.Count);

        // Check domain invariants for every produced Segment
        foreach (var seg in result)
        {
            Assert.True(seg.End > seg.Start);
            foreach (var w in seg.Words)
            {
                Assert.True(w.Start >= seg.Start);
                Assert.True(w.End <= seg.End);
            }
        }
    }
}
