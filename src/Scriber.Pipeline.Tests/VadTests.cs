using Scriber.Core.Audio;
using Scriber.Pipeline.Import;
using Scriber.Pipeline.Vad;
using Xunit;

namespace Scriber.Pipeline.Tests;

/// <summary>
/// VAD tests against the spike-002 synthetic fixture: 8 turns of speech
/// (6.0 s each) separated by 0.5 s silence, exact ground truth
/// (two_speaker_fixture.groundtruth.txt).
/// </summary>
public class VadTests
{
    // Ground-truth speech turns, seconds.
    private static readonly (double Start, double End)[] Turns =
    [
        (0.0, 6.0), (6.5, 12.5), (13.0, 19.0), (19.5, 25.5),
        (26.0, 32.0), (32.5, 38.5), (39.0, 45.0), (45.5, 51.5),
    ];

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static async Task<AudioBuffer> DecodeFixture(string name)
    {
        var decoder = new FfmpegAudioDecoder();
        return await decoder.DecodeAsync(Fixture(name));
    }

    [Fact]
    public async Task DetectSpeech_RecoversAllEightTurns_ExcludingSilenceGaps()
    {
        var audio = await DecodeFixture("two_speaker_fixture.wav");
        var vad = new SherpaVad(Fixture("silero_vad.onnx"));

        var ranges = vad.DetectSpeech(audio);

        Assert.Equal(Turns.Length, ranges.Count);

        for (var i = 0; i < Turns.Length; i++)
        {
            var (start, end) = Turns[i];
            var range = ranges[i];
            // Window quantization (32 ms) + min-silence hysteresis (0.25 s).
            // Alice (turns 0, 2, 4, 6) has ~0.31 s leading silence in the source clip; Carter (turns 1, 3, 5, 7) starts at ~0.08 s.
            Assert.InRange(range.Start.TotalSeconds, start - 0.1, start + 0.35);
            // Alice speech continues to the slice boundary (~6.0 s); Carter stops speaking at ~5.15 s into each turn.
            var minExpectedEnd = i % 2 == 1 ? end - 1.0 : end - 0.1;
            Assert.InRange(range.End.TotalSeconds, minExpectedEnd, end + 0.45);
        }

        // Silence gaps must survive: consecutive ranges do not merge.
        for (var i = 1; i < ranges.Count; i++)
        {
            var gap = ranges[i].Start - ranges[i - 1].End;
            Assert.True(gap >= TimeSpan.FromSeconds(0.1),
                $"gap {i}: {gap.TotalSeconds:0.000}s, expected >= 0.1 s of excluded silence");
        }
    }

    [Fact]
    public async Task DetectSpeech_DoesNotClipSpeechAtFileBoundaries()
    {
        var audio = await DecodeFixture("two_speaker_fixture.wav");
        var vad = new SherpaVad(Fixture("silero_vad.onnx"));

        var ranges = vad.DetectSpeech(audio);

        Assert.NotEmpty(ranges);
        // Speech starts at ~0.318 s in the fixture (Alice clip contains 0.318 s of leading silence):
        // the first range must not miss its head.
        Assert.True(ranges[0].Start <= TimeSpan.FromSeconds(0.35),
            $"first range starts at {ranges[0].Start.TotalSeconds:0.000}s");
        // Speech runs to the last sample (51.5 s): the final range must not
        // lose its tail.
        Assert.True(ranges[^1].End >= TimeSpan.FromSeconds(50.5),
            $"last range ends at {ranges[^1].End.TotalSeconds:0.000}s");
    }

    [Fact]
    public async Task DetectSpeech_AllSilence_ReturnsNoRanges()
    {
        var audio = await DecodeFixture("silence.wav");
        var vad = new SherpaVad(Fixture("silero_vad.onnx"));

        var ranges = vad.DetectSpeech(audio);

        Assert.Empty(ranges);
    }

    [Fact]
    public void DetectSpeech_RejectsNon16kMono()
    {
        var vad = new SherpaVad(Fixture("silero_vad.onnx"));

        var stereo = new AudioBuffer(new float[16000], 16000, 2);
        var ex = Assert.Throws<ArgumentException>(() => vad.DetectSpeech(stereo));
        Assert.Contains("mono", ex.Message, StringComparison.OrdinalIgnoreCase);

        var wrongRate = new AudioBuffer(new float[8000], 8000, 1);
        ex = Assert.Throws<ArgumentException>(() => vad.DetectSpeech(wrongRate));
        Assert.Contains("16000", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_MissingModel_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => new SherpaVad(Fixture("does_not_exist.onnx")));
    }
}
