using Scriber.Core.Audio;
using Scriber.Pipeline.Import;
using Xunit;

namespace Scriber.Pipeline.Tests;

public class AudioDecoderTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static double Rms(AudioBuffer buffer, double fromSeconds, double toSeconds)
    {
        var from = (int)(fromSeconds * buffer.SampleRate);
        var to = (int)(toSeconds * buffer.SampleRate);
        double sum = 0;
        for (var i = from; i < to; i++)
        {
            sum += (double)buffer.Samples[i] * buffer.Samples[i];
        }

        return Math.Sqrt(sum / (to - from));
    }

    [Theory]
    [InlineData("tone.wav")]
    [InlineData("tone.mp3")]
    [InlineData("tone.m4a")]
    [InlineData("tone.mp4")]
    public async Task Decode_SupportedFormats_ProducesNormalizedPcm(string fixture)
    {
        var decoder = new FfmpegAudioDecoder();

        var buffer = await decoder.DecodeAsync(Fixture(fixture));

        Assert.Equal(16000, buffer.SampleRate);
        Assert.Equal(1, buffer.Channels);
        // Fixture is 1.5 s; lossy codecs trim/extend slightly around container granularity.
        Assert.InRange(buffer.Duration.TotalSeconds, 1.35, 1.65);
        // Measured on fixtures: tone region RMS ≈ 0.088 (lavfi sine defaults to 1/8 amplitude),
        // trailing pad is digital silence. Thresholds carry margin for codec noise.
        Assert.InRange(Rms(buffer, 0.1, 0.9), 0.05, 0.15);
        Assert.InRange(Rms(buffer, 1.1, 1.45), 0.0, 0.02);
    }

    [Fact]
    public async Task Decode_NonexistentFile_ThrowsFileNotFound()
    {
        var decoder = new FfmpegAudioDecoder();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => decoder.DecodeAsync(Fixture("does-not-exist.wav")));
    }

    [Fact]
    public async Task Decode_CorruptFile_ThrowsAudioDecodeException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scriber-corrupt-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(path, new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0xFF, 0xFE, 0x12 });

        var decoder = new FfmpegAudioDecoder();
        await Assert.ThrowsAsync<AudioDecodeException>(() => decoder.DecodeAsync(path));
    }

    [Fact]
    public async Task Decode_UnsupportedFormat_ThrowsAudioDecodeException()
    {
        var decoder = new FfmpegAudioDecoder();

        await Assert.ThrowsAsync<AudioDecodeException>(() => decoder.DecodeAsync(Fixture("notes.txt")));
    }

    [Fact]
    public async Task Decode_VideoWithoutAudio_ThrowsAudioDecodeException()
    {
        var decoder = new FfmpegAudioDecoder();

        var ex = await Assert.ThrowsAsync<AudioDecodeException>(() => decoder.DecodeAsync(Fixture("videoonly.mp4")));
        Assert.Contains("no decodable audio stream", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
