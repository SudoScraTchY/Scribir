using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Xunit;

namespace Scriber.Core.Tests;

public class DomainValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static Speaker Speaker() => new("spk-1", "Speaker 1", "#4A90D9");

    private static Word Word(string text, double start, double end, double confidence = 0.9) =>
        new(text, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), confidence);

    private static Segment Segment(double start = 0.0, double end = 2.0, params Word[] words) =>
        new("spk-1", TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), words);

    private static AudioReference Audio() => new("C:/audio/test.wav", TimeSpan.FromSeconds(10), 16000);

    private static Transcript Transcript(params Segment[] segments) =>
        new(Guid.NewGuid(), "Test transcript", Now, "en",
        [Speaker()], segments, Audio());

    [Fact]
    public void Transcript_ValidConstruction_Succeeds()
    {
        var transcript = Transcript(Segment(words: [Word("hello", 0.1, 0.5), Word("world", 0.6, 1.0)]));

        Assert.Equal("Test transcript", transcript.Title);
        Assert.Equal("en", transcript.LanguageCode);
        Assert.Single(transcript.Speakers);
        Assert.Single(transcript.Segments);
        Assert.Equal(2, transcript.Segments[0].Words.Count);
    }

    [Fact]
    public void Transcript_SegmentWithUnknownSpeaker_Throws()
    {
        var segment = Segment();
        // Rebuild with a speaker id that is not declared on the transcript.
        var rogue = segment with { SpeakerId = "spk-ghost" };

        var ex = Assert.Throws<ArgumentException>(() => Transcript(rogue));
        Assert.Contains("spk-ghost", ex.Message);
    }

    [Fact]
    public void Segment_WordOutsideRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => Segment(words: [Word("early", -0.5, -0.1)]));
        Assert.Throws<ArgumentException>(() => Segment(words: [Word("late", 2.5, 3.0)]));
    }

    [Fact]
    public void Segment_WordExactlyOnBoundaries_Succeeds()
    {
        var segment = Segment(words: [Word("edge", 0.0, 2.0)]);
        Assert.Single(segment.Words);
    }

    [Fact]
    public void Segment_WordsOutOfOrder_Throws()
    {
        Assert.Throws<ArgumentException>(() => Segment(words:
        [
            Word("second", 1.0, 1.4),
            Word("first", 0.1, 0.5),
        ]));
    }

    [Fact]
    public void Segment_EndNotAfterStart_Throws()
    {
        Assert.Throws<ArgumentException>(() => Segment(end: 0.0));
        Assert.Throws<ArgumentException>(() => Segment(end: -1.0));
    }

    [Fact]
    public void Word_ConfidenceOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Word("a", 0.0, 0.1, -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => Word("a", 0.0, 0.1, 1.01));
    }

    [Fact]
    public void Word_ConfidenceOnBounds_Succeeds()
    {
        Assert.Equal(0.0, Word("a", 0.0, 0.1, 0.0).Confidence);
        Assert.Equal(1.0, Word("a", 0.0, 0.1, 1.0).Confidence);
    }

    [Fact]
    public void Speaker_InvalidColorHex_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Speaker("s", "S", "4A90D9"));   // missing #
        Assert.Throws<ArgumentException>(() => new Speaker("s", "S", "#4A90"));   // too short
        Assert.Throws<ArgumentException>(() => new Speaker("s", "S", "#GG90D9")); // non-hex
    }

    [Fact]
    public void AudioReference_NonPositiveDurationOrRate_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AudioReference("a.wav", TimeSpan.Zero, 16000));
        Assert.Throws<ArgumentException>(() => new AudioReference("a.wav", TimeSpan.FromSeconds(1), 0));
    }

    [Fact]
    public void AudioBuffer_InvalidConstruction_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AudioBuffer([], 16000, 1));
        Assert.Throws<ArgumentException>(() => new AudioBuffer([0.1f, 0.2f, 0.3f], 16000, 2)); // not whole frames
        Assert.Throws<ArgumentException>(() => new AudioBuffer([0.1f], 0, 1));
        Assert.Throws<ArgumentException>(() => new AudioBuffer([0.1f], 16000, 0));
    }

    [Fact]
    public void AudioBuffer_Duration_AccountsForChannels()
    {
        var mono = new AudioBuffer(new float[16000], 16000, 1);
        var stereo = new AudioBuffer(new float[32000], 16000, 2);

        Assert.Equal(TimeSpan.FromSeconds(1), mono.Duration);
        Assert.Equal(16000, stereo.FrameCount);
        Assert.Equal(TimeSpan.FromSeconds(1), stereo.Duration);
    }
}
