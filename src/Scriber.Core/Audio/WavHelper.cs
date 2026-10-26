using System.Buffers;
using System.Buffers.Binary;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace Scriber.Core.Audio;

/// <summary>
/// Provides high-performance, low-allocation reading and writing of standard 16 kHz 16-bit mono WAV audio.
/// Avoids external process spawns (e.g. ffmpeg) when files are already in WAV format, conserving system memory.
/// </summary>
public static class WavHelper
{
    public const int TargetSampleRate = 16000;
    public const short TargetChannels = 1;
    public const short TargetBitsPerSample = 16;
    public const int HeaderSize = 44;

    /// <summary>
    /// Writes the 44-byte RIFF/WAVE header to a stream for 16 kHz 16-bit mono PCM.
    /// If totalAudioBytes is unknown at start (streaming), pass 0 and call <see cref="FinalizeHeader"/> when done.
    /// </summary>
    public static void WriteHeader(Stream stream, int totalAudioBytes = 0)
    {
        Span<byte> header = stackalloc byte[HeaderSize];

        // "RIFF"
        Encoding.ASCII.GetBytes("RIFF", header[..4]);
        // ChunkSize: 36 + SubChunk2Size
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], 36 + totalAudioBytes);
        // "WAVE"
        Encoding.ASCII.GetBytes("WAVE", header[8..12]);
        // "fmt "
        Encoding.ASCII.GetBytes("fmt ", header[12..16]);
        // Subchunk1Size: 16 for PCM
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], 16);
        // AudioFormat: 1 for PCM
        BinaryPrimitives.WriteInt16LittleEndian(header[20..22], 1);
        // NumChannels: 1
        BinaryPrimitives.WriteInt16LittleEndian(header[22..24], TargetChannels);
        // SampleRate: 16000
        BinaryPrimitives.WriteInt32LittleEndian(header[24..28], TargetSampleRate);
        // ByteRate: SampleRate * NumChannels * BitsPerSample/8 = 32000
        int byteRate = TargetSampleRate * TargetChannels * (TargetBitsPerSample / 8);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..32], byteRate);
        // BlockAlign: NumChannels * BitsPerSample/8 = 2
        short blockAlign = (short)(TargetChannels * (TargetBitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(header[32..34], blockAlign);
        // BitsPerSample: 16
        BinaryPrimitives.WriteInt16LittleEndian(header[34..36], TargetBitsPerSample);
        // "data"
        Encoding.ASCII.GetBytes("data", header[36..40]);
        // Subchunk2Size: totalAudioBytes
        BinaryPrimitives.WriteInt32LittleEndian(header[40..44], totalAudioBytes);

        stream.Write(header);
    }

    /// <summary>
    /// Rewrites the RIFF header chunk sizes once recording/streaming is complete.
    /// Requires a seekable stream (e.g. FileStream).
    /// </summary>
    public static void FinalizeHeader(Stream stream, int totalAudioBytes)
    {
        if (!stream.CanSeek) return;

        long currentPosition = stream.Position;
        Span<byte> intBuffer = stackalloc byte[4];

        // Update ChunkSize at offset 4
        stream.Seek(4, SeekOrigin.Begin);
        BinaryPrimitives.WriteInt32LittleEndian(intBuffer, 36 + totalAudioBytes);
        stream.Write(intBuffer);

        // Update Subchunk2Size at offset 40
        stream.Seek(40, SeekOrigin.Begin);
        BinaryPrimitives.WriteInt32LittleEndian(intBuffer, totalAudioBytes);
        stream.Write(intBuffer);

        stream.Seek(currentPosition, SeekOrigin.Begin);
    }

    /// <summary>
    /// Writes a complete 16 kHz 16-bit mono PCM WAV file from float samples [-1.0, 1.0].
    /// </summary>
    public static void WriteWavFile(string filePath, ReadOnlySpan<float> samples, int sampleRate = TargetSampleRate, short channels = TargetChannels)
    {
        int bytesPerSample = TargetBitsPerSample / 8;
        int totalAudioBytes = samples.Length * channels * bytesPerSample;

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteHeader(fs, totalAudioBytes);

        byte[] pcmBuffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            int pcmIndex = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                short pcm = (short)Math.Clamp(samples[i] * 32767.0f, short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(pcmBuffer.AsSpan(pcmIndex, 2), pcm);
                pcmIndex += 2;

                if (pcmIndex == pcmBuffer.Length)
                {
                    fs.Write(pcmBuffer, 0, pcmIndex);
                    pcmIndex = 0;
                }
            }

            if (pcmIndex > 0)
            {
                fs.Write(pcmBuffer, 0, pcmIndex);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pcmBuffer);
        }
    }

    /// <summary>
    /// Computes the normalized RMS (Root Mean Square) audio amplitude (0.0 to 1.0) from a 16-bit PCM byte span.
    /// Memory efficient: operates purely on the stack/span without heap allocations.
    /// Optimized for high-frequency real-time audio visualization using direct memory reinterpretation and SIMD acceleration.
    /// </summary>
    public static float CalculateRmsLevel(ReadOnlySpan<byte> pcm16Bytes)
    {
        if (pcm16Bytes.Length < 2) return 0f;

        int sampleCount = pcm16Bytes.Length / 2;
        long sumSquares = 0;

        if (BitConverter.IsLittleEndian)
        {
            ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(pcm16Bytes.Slice(0, sampleCount * 2));
            int i = 0;

            if (Vector256.IsHardwareAccelerated && samples.Length >= 8)
            {
                var accLo = Vector256<long>.Zero;
                var accHi = Vector256<long>.Zero;
                int simdLimit = samples.Length - (samples.Length % 8);

                for (; i < simdLimit; i += 8)
                {
                    Vector128<short> v8 = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(samples), (nuint)i);
                    (Vector128<int> loInt, Vector128<int> hiInt) = Vector128.Widen(v8);
                    Vector256<int> vInt8 = Vector256.Create(loInt, hiInt);
                    (Vector256<long> loLong, Vector256<long> hiLong) = Vector256.Widen(vInt8);

                    accLo += loLong * loLong;
                    accHi += hiLong * hiLong;
                }

                sumSquares += Vector256.Sum(accLo) + Vector256.Sum(accHi);
            }
            else if (Vector128.IsHardwareAccelerated && samples.Length >= 8)
            {
                var acc0 = Vector128<long>.Zero;
                var acc1 = Vector128<long>.Zero;
                var acc2 = Vector128<long>.Zero;
                var acc3 = Vector128<long>.Zero;
                int simdLimit = samples.Length - (samples.Length % 8);

                for (; i < simdLimit; i += 8)
                {
                    Vector128<short> v8 = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(samples), (nuint)i);
                    (Vector128<int> loInt, Vector128<int> hiInt) = Vector128.Widen(v8);
                    (Vector128<long> l0, Vector128<long> l1) = Vector128.Widen(loInt);
                    (Vector128<long> h0, Vector128<long> h1) = Vector128.Widen(hiInt);

                    acc0 += l0 * l0;
                    acc1 += l1 * l1;
                    acc2 += h0 * h0;
                    acc3 += h1 * h1;
                }

                sumSquares += Vector128.Sum(acc0) + Vector128.Sum(acc1) + Vector128.Sum(acc2) + Vector128.Sum(acc3);
            }

            for (; i < samples.Length; i++)
            {
                int s = samples[i];
                sumSquares += (long)s * s;
            }
        }
        else
        {
            for (int i = 0; i < sampleCount; i++)
            {
                int s = BinaryPrimitives.ReadInt16LittleEndian(pcm16Bytes.Slice(i * 2, 2));
                sumSquares += (long)s * s;
            }
        }

        double meanSquare = (double)sumSquares / sampleCount;
        double rms = Math.Sqrt(meanSquare) / 32768.0;
        // Apply mild logarithmic gain so normal speech (typically -30dB to -15dB) shows visually on meters
        float visualLevel = (float)Math.Clamp(rms * 4.0, 0.0, 1.0);
        return visualLevel;
    }

    /// <summary>
    /// Computes normalized RMS audio amplitude (0.0 to 1.0) from normalized float32 samples [-1.0, 1.0]
    /// accelerated with SIMD vectorization via <see cref="TensorPrimitives.Norm"/>.
    /// </summary>
    public static float CalculateRmsLevel(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0f;

        float norm = TensorPrimitives.Norm(samples);
        float rms = norm / MathF.Sqrt(samples.Length);
        return Math.Clamp(rms * 4.0f, 0f, 1f);
    }

    /// <summary>
    /// Converts a span of little-endian 16-bit PCM bytes into normalized float32 samples [-1.0, 1.0],
    /// writing the results into the provided destination span.
    /// Accelerated with SIMD vectorization (Vector256 / Vector128) with zero heap allocations.
    /// </summary>
    public static void Pcm16ToFloat(ReadOnlySpan<byte> pcm16Bytes, Span<float> destination)
    {
        int sampleCount = pcm16Bytes.Length / 2;
        if (sampleCount == 0) return;

        if (destination.Length < sampleCount)
        {
            throw new ArgumentException("Destination span is smaller than sample count.", nameof(destination));
        }

        const float scale = 1.0f / 32768.0f;
        int i = 0;

        if (BitConverter.IsLittleEndian)
        {
            ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(pcm16Bytes.Slice(0, sampleCount * 2));

            if (Vector256.IsHardwareAccelerated && samples.Length >= 16)
            {
                var vScale = Vector256.Create(scale);
                int simdLimit = samples.Length - (samples.Length % 16);

                for (; i < simdLimit; i += 16)
                {
                    Vector256<short> vShorts = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(samples), (nuint)i);
                    (Vector256<int> lo, Vector256<int> hi) = Vector256.Widen(vShorts);

                    Vector256<float> flo = Vector256.ConvertToSingle(lo) * vScale;
                    Vector256<float> fhi = Vector256.ConvertToSingle(hi) * vScale;

                    flo.StoreUnsafe(ref MemoryMarshal.GetReference(destination), (nuint)i);
                    fhi.StoreUnsafe(ref MemoryMarshal.GetReference(destination), (nuint)(i + 8));
                }
            }
            else if (Vector128.IsHardwareAccelerated && samples.Length >= 8)
            {
                var vScale = Vector128.Create(scale);
                int simdLimit = samples.Length - (samples.Length % 8);

                for (; i < simdLimit; i += 8)
                {
                    Vector128<short> vShorts = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(samples), (nuint)i);
                    (Vector128<int> lo, Vector128<int> hi) = Vector128.Widen(vShorts);

                    Vector128<float> flo = Vector128.ConvertToSingle(lo) * vScale;
                    Vector128<float> fhi = Vector128.ConvertToSingle(hi) * vScale;

                    flo.StoreUnsafe(ref MemoryMarshal.GetReference(destination), (nuint)i);
                    fhi.StoreUnsafe(ref MemoryMarshal.GetReference(destination), (nuint)(i + 4));
                }
            }

            for (; i < samples.Length; i++)
            {
                destination[i] = samples[i] * scale;
            }
        }
        else
        {
            for (; i < sampleCount; i++)
            {
                short pcm = BinaryPrimitives.ReadInt16LittleEndian(pcm16Bytes.Slice(i * 2, 2));
                destination[i] = pcm * scale;
            }
        }
    }

    /// <summary>
    /// Converts a span of little-endian 16-bit PCM bytes into normalized float32 samples [-1.0, 1.0].
    /// Accelerated with SIMD vectorization.
    /// </summary>
    public static float[] Pcm16ToFloat(ReadOnlySpan<byte> pcm16Bytes)
    {
        if (pcm16Bytes.Length < 2) return [];

        int sampleCount = pcm16Bytes.Length / 2;
        var samples = new float[sampleCount];
        Pcm16ToFloat(pcm16Bytes, samples);
        return samples;
    }

    /// <summary>
    /// Directly reads a standard 16 kHz 16-bit mono PCM WAV file into an <see cref="AudioBuffer"/>,
    /// bypassing ffmpeg process launch overhead and memory consumption.
    /// Returns null if the file is not a supported standard WAV (caller can then fall back to ffmpeg).
    /// </summary>
    public static async Task<AudioBuffer?> TryReadWavDirectAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return null;

        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (fileStream.Length < HeaderSize) return null;

        byte[] header = new byte[HeaderSize];
        int read = await fileStream.ReadAsync(header.AsMemory(0, HeaderSize), ct).ConfigureAwait(false);
        if (read < HeaderSize) return null;

        // Verify RIFF and WAVE signatures
        if (Encoding.ASCII.GetString(header, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(header, 8, 4) != "WAVE" ||
            Encoding.ASCII.GetString(header, 12, 4) != "fmt ")
        {
            return null;
        }

        short audioFormat = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(20, 2));
        short channels = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(22, 2));
        int sampleRate = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24, 4));
        short bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(34, 2));

        // Support PCM (1) or IEEE float (3), mono or stereo, 16-bit or 32-bit float
        if ((audioFormat != 1 && audioFormat != 3) || (channels != 1 && channels != 2) || (audioFormat == 1 && bitsPerSample != 16) || (audioFormat == 3 && bitsPerSample != 32))
        {
            return null; // Let general-purpose ffmpeg handle other exotic formats
        }

        // Find "data" chunk (scan subchunks)
        int dataChunkOffset = 12;
        while (dataChunkOffset + 8 <= fileStream.Length)
        {
            fileStream.Seek(dataChunkOffset, SeekOrigin.Begin);
            await fileStream.ReadAsync(header.AsMemory(0, 8), ct).ConfigureAwait(false);
            string chunkId = Encoding.ASCII.GetString(header, 0, 4);
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));

            if (chunkId == "data")
            {
                int bytesPerFrame = channels * (bitsPerSample / 8);
                if (bytesPerFrame <= 0) return null;
                int frameCount = chunkSize / bytesPerFrame;
                var rawSamples = new float[frameCount];

                // Read in pooled blocks to minimize memory spikes
                byte[] rawBytes = ArrayPool<byte>.Shared.Rent(32768);
                try
                {
                    int framesRead = 0;
                    while (framesRead < frameCount)
                    {
                        int framesToRead = Math.Min(frameCount - framesRead, rawBytes.Length / bytesPerFrame);
                        int bytesToRead = framesToRead * bytesPerFrame;

                        int bytesRead = await fileStream.ReadAsync(rawBytes.AsMemory(0, bytesToRead), ct).ConfigureAwait(false);
                        if (bytesRead <= 0) break;

                        int actualFrames = bytesRead / bytesPerFrame;
                        if (audioFormat == 1 && bitsPerSample == 16)
                        {
                            if (channels == 1)
                            {
                                Pcm16ToFloat(rawBytes.AsSpan(0, actualFrames * 2), rawSamples.AsSpan(framesRead, actualFrames));
                            }
                            else // Stereo 16-bit
                            {
                                for (int i = 0; i < actualFrames; i++)
                                {
                                    short left = BinaryPrimitives.ReadInt16LittleEndian(rawBytes.AsSpan(i * 4, 2));
                                    short right = BinaryPrimitives.ReadInt16LittleEndian(rawBytes.AsSpan(i * 4 + 2, 2));
                                    rawSamples[framesRead + i] = ((left + right) / 2.0f) / 32768.0f;
                                }
                            }
                        }
                        else if (audioFormat == 3 && bitsPerSample == 32)
                        {
                            if (channels == 1)
                            {
                                for (int i = 0; i < actualFrames; i++)
                                {
                                    rawSamples[framesRead + i] = BitConverter.ToSingle(rawBytes.AsSpan(i * 4, 4));
                                }
                            }
                            else // Stereo float32
                            {
                                for (int i = 0; i < actualFrames; i++)
                                {
                                    float left = BitConverter.ToSingle(rawBytes.AsSpan(i * 8, 4));
                                    float right = BitConverter.ToSingle(rawBytes.AsSpan(i * 8 + 4, 4));
                                    rawSamples[framesRead + i] = (left + right) * 0.5f;
                                }
                            }
                        }
                        framesRead += actualFrames;
                    }

                    float[] finalSamples = sampleRate == TargetSampleRate
                        ? rawSamples
                        : ResampleLinear(rawSamples, sampleRate, TargetSampleRate);

                    return new AudioBuffer(finalSamples, TargetSampleRate, TargetChannels);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rawBytes);
                }
            }

            dataChunkOffset += 8 + chunkSize;
        }

        return null;
    }

    private static float[] ResampleLinear(float[] input, int srcRate, int dstRate)
    {
        if (srcRate == dstRate || input.Length == 0) return input;
        int dstLength = (int)((long)input.Length * dstRate / srcRate);
        if (dstLength == 0) return [];
        var output = new float[dstLength];
        double ratio = (double)srcRate / dstRate;
        for (int i = 0; i < dstLength; i++)
        {
            double srcIdx = i * ratio;
            int idxFloor = (int)srcIdx;
            int idxCeil = Math.Min(idxFloor + 1, input.Length - 1);
            float frac = (float)(srcIdx - idxFloor);
            output[i] = input[idxFloor] * (1.0f - frac) + input[idxCeil] * frac;
        }
        return output;
    }
}
