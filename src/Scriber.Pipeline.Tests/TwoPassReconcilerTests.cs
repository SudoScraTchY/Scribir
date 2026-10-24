using Scriber.Core.Domain;
using Scriber.Pipeline.Streaming;
using Xunit;

namespace Scriber.Pipeline.Tests;

public class TwoPassReconcilerTests
{
    private static Transcript CreateTranscript(string title, params Segment[] segments)
    {
        var speakers = new[]
        {
            new Speaker("speaker-0", "Speaker 1", "#2563EB"),
            new Speaker("speaker-1", "Speaker 2", "#16A34A")
        };

        var audioRef = new AudioReference("test.wav", TimeSpan.FromSeconds(10), 16000);

        return new Transcript(
            Guid.NewGuid(),
            title,
            DateTimeOffset.UtcNow,
            "en",
            speakers,
            segments,
            audioRef);
    }

    [Fact]
    public void Reconcile_WhenLiveDraftEmpty_ReturnsOfflineFinalDirectly()
    {
        var reconciler = new TwoPassReconciler();

        var liveDraft = CreateTranscript("Live Session");
        var offlineSeg = new Segment(
            "speaker-0",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2.0),
            [new Word("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(1.0), 0.95), new Word("world", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(2.0), 0.95)]);

        var offlineFinal = CreateTranscript("Offline Pass", offlineSeg);

        var result = reconciler.Reconcile(liveDraft, offlineFinal);

        Assert.Equal(offlineFinal.Id, result.Id);
        Assert.Single(result.Segments);
        Assert.Equal("Hello world", result.Segments[0].Text);
    }

    [Fact]
    public void Reconcile_WhenUserMadeNoEdits_AdoptsOfflineFinalAccuracy()
    {
        var reconciler = new TwoPassReconciler();

        var liveSeg = new Segment(
            "speaker-0",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1.8),
            [new Word("hello", TimeSpan.Zero, TimeSpan.FromSeconds(1.0), 0.8), new Word("world", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.8), 0.8)]);

        var offlineSeg = new Segment(
            "speaker-0",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2.0),
            [new Word("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(1.0), 0.98), new Word("world", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(2.0), 0.99)]);

        var liveDraft = CreateTranscript("Session Draft", liveSeg);
        var offlineFinal = CreateTranscript("Session Final", offlineSeg);

        var result = reconciler.Reconcile(liveDraft, offlineFinal);

        Assert.Single(result.Segments);
        Assert.Equal("Hello world", result.Segments[0].Text);
        Assert.Equal(2, result.Segments[0].Words.Count);
    }

    [Fact]
    public void Reconcile_WhenUserMadeManualEdit_PreservesUserCorrection()
    {
        var reconciler = new TwoPassReconciler();

        // User corrected "Ali said hello to everybody" during live review
        var liveEditedSeg = new Segment(
            "speaker-0",
            TimeSpan.FromSeconds(1.0),
            TimeSpan.FromSeconds(4.0),
            [
                new Word("Ali", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.5), 1.0),
                new Word("said", TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.0), 1.0),
                new Word("hello", TimeSpan.FromSeconds(2.0), TimeSpan.FromSeconds(2.5), 1.0),
                new Word("to", TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(3.0), 1.0),
                new Word("everybody", TimeSpan.FromSeconds(3.0), TimeSpan.FromSeconds(4.0), 1.0)
            ]);

        // Offline model produced raw acoustic guess: "Al said hello to everybody"
        var offlineSeg = new Segment(
            "speaker-0",
            TimeSpan.FromSeconds(1.0),
            TimeSpan.FromSeconds(4.2),
            [
                new Word("Al", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.5), 0.70),
                new Word("said", TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.0), 0.95),
                new Word("hello", TimeSpan.FromSeconds(2.0), TimeSpan.FromSeconds(2.5), 0.95),
                new Word("to", TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(3.0), 0.95),
                new Word("everybody", TimeSpan.FromSeconds(3.0), TimeSpan.FromSeconds(4.0), 0.95)
            ]);

        var liveDraft = CreateTranscript("Live Session", liveEditedSeg);
        var offlineFinal = CreateTranscript("Offline Final", offlineSeg);

        var result = reconciler.Reconcile(liveDraft, offlineFinal);

        Assert.Single(result.Segments);
        // User's explicit manual correction is preserved!
        Assert.Equal("Ali said hello to everybody", result.Segments[0].Text);
    }
}
