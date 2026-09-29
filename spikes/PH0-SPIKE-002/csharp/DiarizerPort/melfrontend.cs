using System;
using System.IO;
using System.Numerics;

namespace DiarizerPort;

/// <summary>
/// C# port of NemotronAsrStreamingFeatureExtractor's mel frontend.
/// Mirrors transformers `_torch_extract_fbank_features`:
///   preemphasis 0.97 -> torch.stft(n_fft=512, win_length=400 (hann, periodic=False,
///   zero-padded symmetrically to n_fft), hop_length=160, pad_mode="constant", center)
///   -> power spectrum -> mel filterbank matmul -> log(x + 2^-24). No normalization.
/// Frame counts follow the extractor's `features_lengths` formulas; the processor
/// trims to the valid frame count, which this implementation produces directly.
/// </summary>
public sealed class MelFrontend
{
    public const int SampleRate = 16000;
    public const int NFft = 512;
    public const int WinLength = 400;
    public const int HopLength = 160;
    public const int MelBins = 128;
    public const float Preemphasis = 0.97f;
    public const float LogGuard = 5.960464477539063e-08f; // 2^-24, from the extractor

    private readonly float[] _window = new float[NFft];
    private readonly float[] _melFilters; // [MelBins * 257] row-major

    public MelFrontend(string assetsDir)
    {
        var win400 = BinIO.ReadFloats(Path.Combine(assetsDir, "window_hann_400.bin"));
        if (win400.Length != WinLength)
            throw new InvalidDataException($"window_hann_400.bin: expected {WinLength} floats, got {win400.Length}");
        int left = (NFft - WinLength) / 2; // 56
        for (int i = 0; i < WinLength; i++) _window[left + i] = win400[i];

        _melFilters = BinIO.ReadFloats(Path.Combine(assetsDir, "mel_filters_128x257.bin"));
        if (_melFilters.Length != MelBins * 257)
            throw new InvalidDataException($"mel_filters_128x257.bin: expected {MelBins * 257} floats, got {_melFilters.Length}");
    }

    /// <summary>Number of valid (kept) frames for an input of `length` samples, given the centering mode.</summary>
    public static int ValidFrameCount(int length, bool center)
    {
        if (center)
        {
            // floor((L + n_fft//2*2 - n_fft) / hop)
            return (length + NFft - NFft) / HopLength; // == floor(L / hop)
        }
        // floor((L - n_fft) / hop) + 1
        return (length - NFft) / HopLength + 1;
    }

    /// <summary>Compute log-mel features [frames, 128] for one chunk of mono 16-kHz samples.</summary>
    public float[,] Compute(float[] samples, bool center)
    {
        int L = samples.Length;
        int valid = ValidFrameCount(L, center);
        if (valid <= 0) return new float[0, MelBins];

        // preemphasis (first sample kept; list-collate padding is absent for exact-length chunks)
        var pre = new float[L];
        pre[0] = samples[0];
        for (int i = 1; i < L; i++) pre[i] = samples[i] - Preemphasis * samples[i - 1];

        int pad = center ? NFft / 2 : 0; // constant zero padding each side
        var mel = new float[valid, MelBins];
        var buf = new Complex[NFft];
        var spec = new Complex[NFft];

        for (int f = 0; f < valid; f++)
        {
            int start = f * HopLength - pad;
            for (int i = 0; i < NFft; i++)
            {
                int idx = start + i;
                float v = (uint)idx < (uint)L ? pre[idx] : 0f;
                buf[i] = new Complex(v * _window[i], 0.0);
            }
            Fft512(buf, spec);

            // mel filterbank on the power spectrum (257 bins)
            for (int m = 0; m < MelBins; m++)
            {
                double acc = 0.0;
                int row = m * 257;
                for (int k = 0; k < 257; k++)
                {
                    double re = spec[k].Real, im = spec[k].Imaginary;
                    double power = re * re + im * im;
                    acc += (double)_melFilters[row + k] * power;
                }
                mel[f, m] = (float)Math.Log(acc + LogGuard);
            }
        }
        return mel;
    }

    // ---- radix-2 iterative FFT for 512 complex points ----
    private static readonly int[] Rev = BuildBitReversal(512);

    private static int[] BuildBitReversal(int n)
    {
        int bits = (int)Math.Log2(n);
        var rev = new int[n];
        for (int i = 0; i < n; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            rev[i] = r;
        }
        return rev;
    }

    private static void Fft512(Complex[] input, Complex[] output)
    {
        for (int i = 0; i < 512; i++) output[i] = input[Rev[i]];

        for (int len = 2; len <= 512; len <<= 1)
        {
            int half = len >> 1;
            double ang = -2.0 * Math.PI / len;
            var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
            for (int i = 0; i < 512; i += len)
            {
                Complex w = Complex.One;
                for (int j = 0; j < half; j++)
                {
                    Complex u = output[i + j];
                    Complex v = output[i + j + half] * w;
                    output[i + j] = u + v;
                    output[i + j + half] = u - v;
                    w *= wlen;
                }
            }
        }
    }
}
