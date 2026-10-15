namespace Scriber.Inference.Diarization.Internal;

internal static class BinIO
{
    public static float[] ReadFloats(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required model asset not found: {path}", path);

        byte[] bytes = File.ReadAllBytes(path);
        var arr = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length - (bytes.Length % 4));
        return arr;
    }
}
