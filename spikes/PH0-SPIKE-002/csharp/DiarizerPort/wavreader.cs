using System;
using System.IO;

namespace DiarizerPort;

/// <summary>Minimal 16-bit PCM / float32 WAV reader (mono or first channel), any sample rate.</summary>
public static class WavReader
{
    public static (float[] samples, int sampleRate) Read(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a RIFF file");
        br.ReadInt32();
        if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a WAVE file");

        int fmtTag = 0, channels = 0, sampleRate = 0, bits = 0;
        long dataPos = -1;
        int dataLen = 0;

        while (fs.Position + 8 <= fs.Length)
        {
            string id = new string(br.ReadChars(4));
            int size = br.ReadInt32();
            long next = fs.Position + size + (size % 2);
            if (id == "fmt ")
            {
                fmtTag = br.ReadInt16();
                channels = br.ReadInt16();
                sampleRate = br.ReadInt32();
                br.ReadInt32();
                br.ReadInt16();
                bits = br.ReadInt16();
            }
            else if (id == "data")
            {
                dataPos = fs.Position;
                dataLen = size;
            }
            fs.Position = next;
            if (dataPos >= 0 && fmtTag != 0) break;
        }
        if (dataPos < 0 || fmtTag == 0) throw new InvalidDataException("missing fmt/data chunk");
        fs.Position = dataPos;
        byte[] raw = br.ReadBytes(dataLen);

        int bytesPerSample = bits / 8;
        int frames = (dataLen / bytesPerSample) / Math.Max(1, channels);
        var samples = new float[frames];

        if (fmtTag == 1 && bits == 16)
        {
            var shorts = new short[frames * channels];
            Buffer.BlockCopy(raw, 0, shorts, 0, frames * channels * 2);
            for (int i = 0; i < frames; i++) samples[i] = shorts[i * channels] / 32768f;
        }
        else if (fmtTag == 3 && bits == 32)
        {
            var floats = new float[frames * channels];
            Buffer.BlockCopy(raw, 0, floats, 0, frames * channels * 4);
            for (int i = 0; i < frames; i++) samples[i] = floats[i * channels];
        }
        else
        {
            throw new NotSupportedException($"unsupported WAV format: tag={fmtTag} bits={bits}");
        }
        return (samples, sampleRate);
    }
}
