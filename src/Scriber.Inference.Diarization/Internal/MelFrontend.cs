using System.Numerics;
using System.Numerics.Tensors;

namespace Scriber.Inference.Diarization.Internal;

/// <summary>
/// C# port of NemotronAsrStreamingFeatureExtractor's mel frontend.
/// Preemphasis 0.97 -> STFT (n_fft=512, win_length=400 Hann, hop_length=160)
/// -> power spectrum -> mel filterbank (128 bins) -> log(x + 2^-24).
/// </summary>
internal sealed class MelFrontend
{
    public const int SampleRate = 16000;
    public const int NFft = 512;
    public const int WinLength = 400;
    public const int HopLength = 160;
    public const int MelBins = 128;
    public const float Preemphasis = 0.97f;
    public const float LogGuard = 5.960464477539063e-08f; // 2^-24

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

    /// <summary>Number of valid frames for an input of length samples.</summary>
    public static int ValidFrameCount(int length, bool center)
    {
        if (center)
        {
            return (length + NFft - NFft) / HopLength; // == floor(L / hop)
        }
        return (length - NFft) / HopLength + 1;
    }

    /// <summary>Compute log-mel features [frames, 128] for one chunk of mono 16-kHz samples.</summary>
    public float[,] Compute(float[] samples, bool center)
    {
        int length = samples.Length;
        int valid = ValidFrameCount(length, center);
        if (valid <= 0) return new float[0, MelBins];

        var pre = new float[length];
        pre[0] = samples[0];
        for (int i = 1; i < length; i++) pre[i] = samples[i] - Preemphasis * samples[i - 1];

        int pad = center ? NFft / 2 : 0;
        var mel = new float[valid, MelBins];
        var buf = new Complex[NFft];
        var spec = new Complex[NFft];
        Span<float> power = stackalloc float[257];

        for (int f = 0; f < valid; f++)
        {
            int start = f * HopLength - pad;
            for (int i = 0; i < NFft; i++)
            {
                int idx = start + i;
                float v = (uint)idx < (uint)length ? pre[idx] : 0f;
                buf[i] = new Complex(v * _window[i], 0.0);
            }
            Fft512(buf, spec);

            for (int k = 0; k < 257; k++)
            {
                float re = (float)spec[k].Real;
                float im = (float)spec[k].Imaginary;
                power[k] = re * re + im * im;
            }

            ReadOnlySpan<float> filtersSpan = _melFilters.AsSpan();
            for (int m = 0; m < MelBins; m++)
            {
                ReadOnlySpan<float> filterRow = filtersSpan.Slice(m * 257, 257);
                float acc = TensorPrimitives.Dot(filterRow, power);
                mel[f, m] = MathF.Log(acc + LogGuard);
            }
        }
        return mel;
    }

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
