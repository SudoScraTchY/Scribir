using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DiarizerPort;

/// <summary>
/// PH0-SPIKE-002 Task A console: C# diarizer port with validation gates against the
/// PH0-SPIKE-001 Python reference artifacts.
///   --check             run gates 1-4; non-zero exit on failure
///   --audio &lt;wav&gt;      run the pipeline and print/write segments
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string repoRoot = FindRepoRoot();
        string assets = Path.Combine(repoRoot, "spikes", "PH0-SPIKE-001", "artifacts", "csharp-port-assets");
        string onnx = Path.Combine(repoRoot, "spikes", "PH0-SPIKE-001", "artifacts", "onnx", "nemotron3_stream_step.onnx");
        string fixtures = Path.Combine(repoRoot, "spikes", "PH0-SPIKE-001", "artifacts", "csharp-fixtures", "fixture");
        string audioDir = Path.Combine(repoRoot, "spikes", "PH0-SPIKE-002", "artifacts", "audio");
        string runsDir = Path.Combine(repoRoot, "spikes", "PH0-SPIKE-002", "csharp", "DiarizerPort", "runs");

        if (args.Length == 0)
        {
            Console.WriteLine("usage: DiarizerPort --check | --audio <file.wav> [--out <dir>]");
            return 2;
        }

        if (args[0] == "--check")
        {
            return RunChecks(assets, onnx, fixtures, audioDir, runsDir);
        }

        if (args[0] == "--audio")
        {
            string wav = args[1];
            string outDir = args.Length > 3 && args[2] == "--out" ? args[3] : Path.Combine(runsDir, Path.GetFileNameWithoutExtension(wav));
            using var d = new Diarizer(assets, onnx);
            var (samples, sr) = WavReader.Read(wav);
            if (sr != 16000) throw new InvalidOperationException($"expected 16 kHz, got {sr}");
            var res = d.Run(samples);
            Console.WriteLine($"audio: {wav}  ({samples.Length / 16000.0:0.00}s, {res.NumChunks} chunks, {res.RuntimeMs} ms)");
            Console.WriteLine($"segments: {res.Segments.Count}");
            foreach (var s in res.Segments)
                Console.WriteLine($"  [{s.Start,6:0.00} - {s.End,6:0.00}] Speaker {s.Speaker}");
            Directory.CreateDirectory(outDir);
            var json = new
            {
                audio = wav,
                sampleRate = sr,
                numChunks = res.NumChunks,
                runtimeMs = res.RuntimeMs,
                totalFrames = res.TotalFrames,
                segments = res.Segments.Select(s => new { start = s.Start, end = s.End, speaker = s.Speaker }),
            };
            File.WriteAllText(Path.Combine(outDir, "segments.json"),
                JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"wrote: {Path.Combine(outDir, "segments.json")}");
            return 0;
        }

        Console.WriteLine("unknown mode");
        return 2;
    }

    private static int RunChecks(string assets, string onnx, string fixtures, string audioDir, string runsDir)
    {
        var report = new List<string>();
        bool pass = true;
        void Line(string s) { Console.WriteLine(s); report.Add(s); }
        void Gate(string name, bool ok, string detail) { pass &= ok; Line($"[{(ok ? "PASS" : "FAIL")}] {name}: {detail}"); }

        using var d = new Diarizer(assets, onnx);

        // step00/step40 reference vectors carry the audio; use them directly
        float[] a00 = BinIO.ReadFloats(Path.Combine(assets, "ref_step00_audio.bin"));
        float[] m00 = BinIO.ReadFloats(Path.Combine(assets, "ref_step00_mel_1xTmx128.bin"));
        float[] a40 = BinIO.ReadFloats(Path.Combine(assets, "ref_step40_audio.bin"));
        float[] m40 = BinIO.ReadFloats(Path.Combine(assets, "ref_step40_mel_1xTmx128.bin"));

        var mel = new MelFrontend(assets);
        var cm00 = mel.Compute(a00, center: true);
        var cm40 = mel.Compute(a40, center: false);
        float d00 = MaxAbsPadded(cm00, m00);
        float d40 = MaxAbsPadded(cm40, m40);
        Gate("gate1a mel(step00,center) vs ref", d00 < 2e-4f, $"maxAbs={d00:E3} (frames {cm00.GetLength(0)} vs {m00.Length / 128})");
        Gate("gate1b mel(step40,no-center) vs ref", d40 < 2e-4f, $"maxAbs={d40:E3} (frames {cm40.GetLength(0)} vs {m40.Length / 128})");

        // ---- gate 2: embedder (from reference mel) ----
        var emb = new Embedder(assets);
        float[] e00ref = BinIO.ReadFloats(Path.Combine(assets, "ref_step00_chunk_embeds_1xEx512.bin"));
        float[] e40ref = BinIO.ReadFloats(Path.Combine(assets, "ref_step40_chunk_embeds_1xEx512.bin"));
        var ce00 = emb.Forward(RefMel(m00), out int E00);
        var ce40 = emb.Forward(RefMel(m40), out int E40);
        float de00 = MaxAbs(ce00, e00ref);
        float de40 = MaxAbs(ce40, e40ref);
        Gate("gate2a embedder(step00) vs ref", de00 < 2e-4f, $"maxAbs={de00:E3} (E={E00})");
        Gate("gate2b embedder(step40) vs ref", de40 < 2e-4f, $"maxAbs={de40:E3} (E={E40})");

        // ---- gate 3: full-stream replay against fixtures ----
        {
            var shapes = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "shapes.json"))).RootElement;
            int numSteps = shapes.GetProperty("num_steps").GetInt32();
            var steps = shapes.GetProperty("steps").EnumerateArray().ToList();
            int firstCompressed = steps.Where(s => s.GetProperty("compressed_after").GetBoolean())
                                       .Select(s => s.GetProperty("i").GetInt32()).DefaultIfEmpty(int.MaxValue).Min();

            var cache = new SpeakerCache();
            float worstCachedBefore = 0, worstCachedAfter = 0, worstLogits = 0;
            for (int i = 0; i < numSteps; i++)
            {
                var st = steps[i];
                int tm = st.GetProperty("tm").GetInt32();
                int L = st.GetProperty("L").GetInt32();
                int outFrames = st.GetProperty("out_frames").GetInt32();
                bool isLast = i == numSteps - 1;

                float[] feats = BinIO.ReadFloats(Path.Combine(fixtures, $"step_{i:000}_feats.bin"));
                float[] cachedFixture = BinIO.ReadFloats(Path.Combine(fixtures, $"step_{i:000}_cached.bin"));
                float[] logitsFixture = BinIO.ReadFloats(Path.Combine(fixtures, $"step_{i:000}_logits.bin"));

                var cachedOurs = cache.GetEmbeds();
                if (cachedOurs.Length != cachedFixture.Length)
                    throw new InvalidOperationException($"step {i}: cached length {cachedOurs.Length} != fixture {cachedFixture.Length}");
                float dc = MaxAbs(cachedOurs, cachedFixture);
                if (i < firstCompressed) worstCachedBefore = Math.Max(worstCachedBefore, dc);
                else worstCachedAfter = Math.Max(worstCachedAfter, dc);

                // graph step using the fixture cached embeds (slicing consistency)
                var feats2d = To2D(feats, tm, 128);
                var full = d.RunGraph(feats2d, tm, cachedFixture, L);
                var slice = new float[outFrames * 8];
                Array.Copy(full, L * 8 * 8, slice, 0, outFrames * 8);
                float dl = MaxAbs(slice, logitsFixture);
                worstLogits = Math.Max(worstLogits, dl);

                // update our cache with our own embedder output
                var e = emb.Forward(feats2d, out int E);
                int numChunkFrames = isLast ? E : E - Diarizer.Right;
                var chunkInput = new float[(L + E) * 512];
                Array.Copy(cachedOurs, 0, chunkInput, 0, L * 512);
                Array.Copy(e, 0, chunkInput, L * 512, E * 512);
                int logitFrames = (L + E) * 8;
                cache.Update(chunkInput, L + E, full, logitFrames, BinIO.ReadFloats(Path.Combine(assets, "silence_embeds_512.bin")), numChunkFrames);
            }
            Gate("gate3a cached_embeds replay (steps before first compression)", worstCachedBefore < 5e-4f, $"worst maxAbs={worstCachedBefore:E3} (tolerance 5e-4; torch float32 noise floor ~1.5e-4 measured)");
            Gate("gate3b cached_embeds replay (compressed steps)", worstCachedAfter < 5e-4f, $"worst maxAbs={worstCachedAfter:E3} (tolerance 5e-4; torch float32 noise floor ~1.5e-4 measured)");
            Gate("gate3c graph sliced logits vs fixture", worstLogits < 1e-4f, $"worst maxAbs={worstLogits:E3}");
        }

        // ---- gate 4: segments end-to-end on the fixture ----
        {
            var (samples, _) = WavReader.Read(Path.Combine(audioDir, "two_speaker_fixture.wav"));
            var res = d.Run(samples);
            var py = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "python_segments.json"))).RootElement
                                   .GetProperty("segments").EnumerateArray().ToList();
            bool ok = res.Segments.Count == py.Count;
            string detail = $"c#={res.Segments.Count} python={py.Count}";
            if (ok)
            {
                for (int i = 0; i < py.Count; i++)
                {
                    double ps = py[i].GetProperty("Start").GetDouble();
                    double pe = py[i].GetProperty("End").GetDouble();
                    int pspk = py[i].GetProperty("Speaker").GetInt32();
                    var cs = res.Segments[i];
                    if (Math.Abs(cs.Start - ps) > 0.02 || Math.Abs(cs.End - pe) > 0.02 || cs.Speaker != pspk)
                    { ok = false; detail += $" | mismatch #{i}: c#({cs.Start},{cs.End},spk{cs.Speaker}) vs py({ps},{pe},spk{pspk})"; break; }
                }
            }
            Gate("gate4 segments end-to-end vs python_segments.json", ok, detail);
            Console.WriteLine("  segments (C#):");
            foreach (var s in res.Segments) Console.WriteLine($"    [{s.Start,6:0.00} - {s.End,6:0.00}] Speaker {s.Speaker}");
        }

        Directory.CreateDirectory(Path.Combine(runsDir, "check"));
        File.WriteAllLines(Path.Combine(runsDir, "check", "check_report.txt"), report);
        Line(pass ? "ALL CHECKS PASSED" : "CHECKS FAILED");
        return pass ? 0 : 1;
    }

    private static float[,] RefMel(float[] flat)
    {
        int frames = flat.Length / 128;
        var m = new float[frames, 128];
        for (int i = 0; i < frames; i++)
            for (int j = 0; j < 128; j++)
                m[i, j] = flat[i * 128 + j];
        return m;
    }

    private static float[,] To2D(float[] flat, int frames, int cols)
    {
        var m = new float[frames, cols];
        for (int i = 0; i < frames; i++)
            for (int j = 0; j < cols; j++)
                m[i, j] = flat[i * cols + j];
        return m;
    }

    private static float MaxAbs(float[] a, float[] b)
    {
        if (a.Length != b.Length) throw new InvalidOperationException($"length mismatch {a.Length} != {b.Length}");
        float m = 0;
        for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Abs(a[i] - b[i]));
        return m;
    }

    private static float MaxAbsPadded(float[,] a, float[] flatB)
    {
        int frames = flatB.Length / 128;
        if (a.GetLength(0) != frames) throw new InvalidOperationException($"frame mismatch {a.GetLength(0)} != {frames}");
        float m = 0;
        for (int i = 0; i < frames; i++)
            for (int j = 0; j < 128; j++)
                m = Math.Max(m, Math.Abs(a[i, j] - flatB[i * 128 + j]));
        return m;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("repo root (.git) not found");
        return dir.FullName;
    }
}
