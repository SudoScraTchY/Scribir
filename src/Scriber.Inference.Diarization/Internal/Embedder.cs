namespace Scriber.Inference.Diarization.Internal;

/// <summary>
/// Feature-stacking embedder: pad mel frames to a multiple of 8, reshape to 1024, linear projection (no bias).
/// </summary>
internal sealed class Embedder
{
    public const int Hidden = 512;
    private readonly float[] _w; // [512 x 1024] row-major

    public Embedder(string assetsDir)
    {
        _w = BinIO.ReadFloats(Path.Combine(assetsDir, "embedder_projection_weight_512x1024.bin"));
        if (_w.Length != Hidden * 1024)
            throw new InvalidDataException($"embedder weight: expected {Hidden * 1024} floats, got {_w.Length}");
    }

    /// <summary>mel [tm, 128] -> [E, 512] with E = ceil(tm / 8).</summary>
    public float[] Forward(float[,] mel, out int e)
    {
        int tm = mel.GetLength(0);
        e = (tm + 7) / 8;
        var outp = new float[e * Hidden];
        var vec = new double[1024];

        for (int g = 0; g < e; g++)
        {
            Array.Clear(vec);
            for (int j = 0; j < 8; j++)
            {
                int fr = g * 8 + j;
                if (fr >= tm) break;
                int off = j * 128;
                for (int m = 0; m < 128; m++) vec[off + m] = mel[fr, m];
            }
            for (int o = 0; o < Hidden; o++)
            {
                double acc = 0.0;
                int row = o * 1024;
                for (int k2 = 0; k2 < 1024; k2++) acc += (double)_w[row + k2] * vec[k2];
                outp[g * Hidden + o] = (float)acc;
            }
        }
        return outp;
    }
}
