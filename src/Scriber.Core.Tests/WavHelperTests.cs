using Scriber.Core.Audio;
using Xunit;

namespace Scriber.Core.Tests;

public class WavHelperTests
{
    [Fact]
    public void CalculateRmsLevel_SilentBuffer_ReturnsZero()
    {
        byte[] silence = new byte[3200]; // 100ms of silence
        float rms = WavHelper.CalculateRmsLevel(silence);
        Assert.Equal(0f, rms);
    }

    [Fact]
    public void CalculateRmsLevel_LoudSignal_ReturnsNonZero()
    {
        byte[] signal = new byte[3200];
        // Populate with alternating high-amplitude 16-bit PCM
        for (int i = 0; i < signal.Length / 2; i++)
        {
            short sample = (short)(i % 2 == 0 ? 20000 : -20000);
            BitConverter.GetBytes(sample).CopyTo(signal, i * 2);
        }

        float rms = WavHelper.CalculateRmsLevel(signal);
        Assert.True(rms > 0.5f, $"Expected high RMS, got {rms}");
    }

    [Fact]
    public void CalculateRmsLevel_FloatSpan_CalculatesAccurately()
    {
        float[] silent = new float[1600];
        Assert.Equal(0f, WavHelper.CalculateRmsLevel(silent));

        float[] loud = new float[1600];
        Array.Fill(loud, 0.5f);
        float rms = WavHelper.CalculateRmsLevel(loud);
        // rms = 0.5, clamped(0.5 * 4.0) = 1.0 (or 2.0 clamped to 1.0)
        Assert.Equal(1.0f, rms);
    }

    [Fact]
    public async Task WriteAndReadWav_RoundTrip_PreservesAudio()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.wav");
        try
        {
            // Write 1 second of 16kHz audio (16,000 samples = 32,000 bytes)
            await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                WavHelper.WriteHeader(fs, 0); // initial header
                byte[] samples = new byte[32000];
                for (int i = 0; i < 16000; i++)
                {
                    short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 16000) * 16000);
                    BitConverter.GetBytes(sample).CopyTo(samples, i * 2);
                }
                await fs.WriteAsync(samples);
                WavHelper.FinalizeHeader(fs, samples.Length);
            }

            var audioBuffer = await WavHelper.TryReadWavDirectAsync(tempFile);

            Assert.NotNull(audioBuffer);
            Assert.Equal(16000, audioBuffer.SampleRate);
            Assert.Equal(1, audioBuffer.Channels);
            Assert.Equal(16000, audioBuffer.FrameCount);
            Assert.InRange(audioBuffer.Duration.TotalSeconds, 0.99, 1.01);
            // Verify samples range between -1 and 1
            Assert.InRange(audioBuffer.Samples[0], -0.1f, 0.1f);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task WriteWavFile_CreatesValidWavFile_ReadableByTryReadWavDirectAsync()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"test_writewav_{Guid.NewGuid():N}.wav");
        try
        {
            float[] samples = new float[16000];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 16000) * 0.5f;
            }

            WavHelper.WriteWavFile(tempFile, samples);

            var audioBuffer = await WavHelper.TryReadWavDirectAsync(tempFile);
            Assert.NotNull(audioBuffer);
            Assert.Equal(16000, audioBuffer.SampleRate);
            Assert.Equal(1, audioBuffer.Channels);
            Assert.Equal(16000, audioBuffer.FrameCount);
            Assert.Equal(samples[100], audioBuffer.Samples[100], 0.01f);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
