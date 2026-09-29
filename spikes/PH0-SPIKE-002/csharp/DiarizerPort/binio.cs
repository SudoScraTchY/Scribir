using System;
using System.IO;

namespace DiarizerPort;

/// <summary>Little-endian float32 tensor IO shared by the reference assets and runs.</summary>
public static class BinIO
{
    public static float[] ReadFloats(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var arr = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length - (bytes.Length % 4));
        return arr;
    }

    public static void WriteFloats(string path, float[] data)
    {
        byte[] bytes = new byte[data.Length * 4];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        File.WriteAllBytes(path, bytes);
    }
}
