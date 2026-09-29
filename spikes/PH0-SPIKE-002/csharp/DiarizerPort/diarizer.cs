using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DiarizerPort;

public sealed record Segment(double Start, double End, int Speaker);

/// <summary>Full C# diarization driver: chunk loop, mel, embedder, ONNX step graph, AOSC cache, segments.</summary>
public sealed class Diarizer : IDisposable
{
    public const int Right = 4;              // low_latency look-ahead encoder frames
    public const int FirstChunkSamples = 16680;
    public const int PerChunkSamples = 17040;
    public const int MelFramesPerStep = 72;
    public const int Hidden = 512;

    private readonly MelFrontend _mel;
    private readonly Embedder _embedder;
    private readonly InferenceSession _sess;
    private readonly float[] _silence;

    public Diarizer(string assetsDir, string onnxPath)
    {
        _mel = new MelFrontend(assetsDir);
        _embedder = new Embedder(assetsDir);
        _silence = BinIO.ReadFloats(Path.Combine(assetsDir, "silence_embeds_512.bin"));
        _sess = new InferenceSession(onnxPath);
    }

    public static List<(int start, int len, bool first, bool last)> EnumerateChunks(int total)
    {
        var chunks = new List<(int, int, bool, bool)>
        {
            (0, Math.Min(FirstChunkSamples, total), true, false)
        };
        int melCursor = MelFramesPerStep;
        int s = melCursor * 160 - 256;
        while (s + PerChunkSamples <= total)
        {
            chunks.Add((s, PerChunkSamples, false, false));
            melCursor += MelFramesPerStep;
            s = melCursor * 160 - 256;
        }
        if (s < total) chunks.Add((s, total - s, false, true));
        return chunks;
    }

    public sealed class RunResult
    {
        public List<Segment> Segments = new();
        public int NumChunks;
        public long RuntimeMs;
        public int TotalFrames;
    }

    public RunResult Run(float[] audio)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int n = audio.Length;
        var chunks = EnumerateChunks(n);
        var cache = new SpeakerCache();
        var pieces = new List<float[]>();

        foreach (var ch in chunks)
        {
            var slice = new float[ch.len];
            Array.Copy(audio, ch.start, slice, 0, ch.len);

            var mel = _mel.Compute(slice, ch.first);
            int tm = mel.GetLength(0);
            if (tm <= 0) throw new InvalidOperationException($"chunk {ch.start}: no valid mel frames");

            var emb = _embedder.Forward(mel, out int e);
            var cached = cache.GetEmbeds();
            int L = cache.StateLength;
            var full = RunGraph(mel, tm, cached, L); // [(L+e)*8, 8]

            int numChunkFrames = ch.last ? e : e - Right;
            int fromFrame = L * 8;
            int count = Math.Min(numChunkFrames * 8, tm);
            var frames = new float[count * 8];
            Array.Copy(full, fromFrame * 8, frames, 0, count * 8);
            pieces.Add(frames);

            var chunkInput = new float[(L + e) * Hidden];
            Array.Copy(cached, 0, chunkInput, 0, L * Hidden);
            Array.Copy(emb, 0, chunkInput, L * Hidden, e * Hidden);
            cache.Update(chunkInput, L + e, full, (L + e) * 8, _silence, numChunkFrames);
        }

        int totalFrames = pieces.Sum(p => p.Length / 8);
        var cat = new float[totalFrames * 8];
        int off = 0;
        foreach (var p in pieces) { Array.Copy(p, 0, cat, off, p.Length); off += p.Length; }

        sw.Stop();
        return new RunResult
        {
            Segments = ExtractSegments(cat, totalFrames),
            NumChunks = chunks.Count,
            RuntimeMs = sw.ElapsedMilliseconds,
            TotalFrames = totalFrames,
        };
    }

    public float[] RunGraph(float[,] mel, int tm, float[] cached, int L)
    {
        var feats = new float[tm * 128];
        for (int i = 0; i < tm; i++)
            for (int m = 0; m < 128; m++)
                feats[i * 128 + m] = mel[i, m];

        var tFeats = new DenseTensor<float>(feats, new[] { 1, tm, 128 });
        var tCached = new DenseTensor<float>(cached, new[] { 1, L, 512 });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_features", tFeats),
            NamedOnnxValue.CreateFromTensor("cached_embeds", tCached),
        };
        using var outputs = _sess.Run(inputs);
        var outT = outputs.First(o => o.Name == "chunk_logits").AsTensor<float>();
        return outT.ToArray();
    }

    /// <summary>Per-speaker runs of sigmoid(logit) &gt; threshold; sorted by (start, speaker). Times rounded to 2 decimals.</summary>
    public static List<Segment> ExtractSegments(float[] logitsFlat, int frames, int numSpeakers = 8, float threshold = 0.5f)
    {
        var segs = new List<Segment>();
        for (int s = 0; s < numSpeakers; s++)
        {
            bool inRun = false;
            int runStart = 0;
            for (int t = 0; t < frames; t++)
            {
                float v = logitsFlat[t * numSpeakers + s];
                bool active = 1.0 / (1.0 + Math.Exp(-v)) > threshold;
                if (active && !inRun) { inRun = true; runStart = t; }
                else if (!active && inRun)
                {
                    inRun = false;
                    segs.Add(new Segment(Math.Round(runStart * 0.01, 2), Math.Round(t * 0.01, 2), s));
                }
            }
            if (inRun) segs.Add(new Segment(Math.Round(runStart * 0.01, 2), Math.Round(frames * 0.01, 2), s));
        }
        segs.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Speaker.CompareTo(b.Speaker));
        return segs;
    }

    public void Dispose() => _sess.Dispose();
}
