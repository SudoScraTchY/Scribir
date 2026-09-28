// PH0-SPIKE-001 — raw ONNX Runtime (C#) replay of the exported Nemotron step graph.
//
// Replays the per-step fixtures produced by 05_export_csharp_fixtures.py:
//   * runs every step through Microsoft.ML.OnnxRuntime (dynamic shapes honored),
//   * compares logits against the Python-ORT expected outputs,
//   * recomputes speaker segments in C# from the concatenated logits and
//     compares them with the Python segments.
//
// Usage: OrtRunner <fixturesDir> <onnxPath> <outJson>

using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string fixtures = args.Length > 0 ? args[0] : @"M:\Agentic\Projects\Scribir\spikes\PH0-SPIKE-001\artifacts\csharp-fixtures\fixture";
        string onnx = args.Length > 1 ? args[1] : @"M:\Agentic\Projects\Scribir\spikes\PH0-SPIKE-001\artifacts\onnx\nemotron3_stream_step.onnx";
        string outJson = args.Length > 2 ? args[2] : Path.Combine(fixtures, "csharp_results.json");

        var shapesDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "shapes.json")));
        var shapes = shapesDoc.RootElement;
        int numSteps = shapes.GetProperty("num_steps").GetInt32();
        int totalFrames = shapes.GetProperty("total_frames").GetInt32();

        using var session = new InferenceSession(onnx);
        Console.WriteLine("onnxruntime C# session created. inputs: " +
            string.Join(", ", session.InputMetadata.Select(kv => $"{kv.Key}[{string.Join(",", kv.Value.Dimensions)}]")));

        float overallMax = 0f;
        var steps = new List<Dictionary<string, object>>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var stepEl in shapes.GetProperty("steps").EnumerateArray())
        {
            int i = stepEl.GetProperty("i").GetInt32();
            int tm = stepEl.GetProperty("tm").GetInt32();
            int L = stepEl.GetProperty("L").GetInt32();

            float[] feats = ReadFloats(Path.Combine(fixtures, $"step_{i:000}_feats.bin"));
            float[] cached = ReadFloats(Path.Combine(fixtures, $"step_{i:000}_cached.bin"));
            float[] expected = ReadFloats(Path.Combine(fixtures, $"step_{i:000}_logits.bin"));

            var tFeats = new DenseTensor<float>(feats, new[] { 1, tm, 128 });
            var tCached = new DenseTensor<float>(cached, new[] { 1, L, 512 });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_features", tFeats),
                NamedOnnxValue.CreateFromTensor("cached_embeds", tCached)
            };

            using var outputs = session.Run(inputs);
            var logits = outputs.First(o => o.Name == "chunk_logits").AsTensor<float>();
            var arr = logits.ToArray();

            // The graph returns the FULL chunk output [(L + E) * 8, 8] where E = ceil(tm/8).
            // The fixtures store the SLICED view the reference pipeline feeds the cache:
            // frames [L*8, L*8 + out_frames).
            int padded = tm + ((8 - (tm % 8)) % 8);
            int E = padded / 8;
            if (arr.Length != (L + E) * 8 * 8)
                throw new Exception($"step {i}: unexpected full output length {arr.Length}, expected {(L + E) * 8 * 8}");
            int startOff = L * 8 * 8;   // (L encoder frames * 8 frames/frame) * 8 speakers
            if (startOff + expected.Length > arr.Length)
                throw new Exception($"step {i}: slice out of range");

            float maxd = 0f;
            for (int k = 0; k < expected.Length; k++)
            {
                float d = Math.Abs(arr[startOff + k] - expected[k]);
                if (d > maxd) maxd = d;
            }
            overallMax = Math.Max(overallMax, maxd);
            steps.Add(new Dictionary<string, object>
            {
                ["i"] = i, ["tm"] = tm, ["L"] = L,
                ["full_out_frames"] = arr.Length / 8,
                ["sliced_out_frames"] = expected.Length / 8,
                ["max_abs_diff"] = maxd
            });
            if (i % 20 == 0 || i == numSteps - 1)
                Console.WriteLine($"step {i:3d}: L={L,3} tm={tm,4} out_frames={arr.Length / 8,4} maxdiff={maxd:E2}");
        }
        sw.Stop();
        Console.WriteLine($"all {numSteps} steps run in {sw.ElapsedMilliseconds} ms; overall max abs diff = {overallMax:E3}");

        // --- segments computed in C# from the concatenated logits ---
        float[] allLogits = ReadFloats(Path.Combine(fixtures, "all_logits.bin"));
        if (allLogits.Length != totalFrames * 8)
            throw new Exception($"all_logits length {allLogits.Length} != total_frames*8 {totalFrames * 8}");

        var segs = new List<(int speaker, int startF, int endF)>();
        for (int spk = 0; spk < 8; spk++)
        {
            bool inRun = false; int runStart = 0;
            for (int t = 0; t < totalFrames; t++)
            {
                float v = allLogits[t * 8 + spk];
                bool active = (1.0 / (1.0 + Math.Exp(-v))) > 0.5;
                if (active && !inRun) { inRun = true; runStart = t; }
                else if (!active && inRun) { inRun = false; segs.Add((spk, runStart, t)); }
            }
            if (inRun) segs.Add((spk, runStart, totalFrames));
        }
        segs.Sort((a, b) => a.startF != b.startF ? a.startF.CompareTo(b.startF) : a.speaker.CompareTo(b.speaker));

        var pyDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "python_segments.json")));
        var pySegs = pyDoc.RootElement.GetProperty("segments").EnumerateArray()
            .Select(s => (Speaker: s.GetProperty("Speaker").GetInt32(),
                          Start: s.GetProperty("Start").GetDouble(),
                          End: s.GetProperty("End").GetDouble()))
            .ToList();

        Console.WriteLine($"segments: C#={segs.Count}  python={pySegs.Count}");
        bool match = segs.Count == pySegs.Count;
        var mismatches = new List<string>();
        if (match)
        {
            for (int k = 0; k < segs.Count; k++)
            {
                double cs = segs[k].startF * 0.01, ce = segs[k].endF * 0.01;
                var ps = pySegs[k];
                if (segs[k].speaker != ps.Speaker || Math.Abs(cs - ps.Start) > 0.02 || Math.Abs(ce - ps.End) > 0.02)
                {
                    match = false;
                    mismatches.Add($"#{k}: c#({cs:0.00}-{ce:0.00} spk{segs[k].speaker}) vs py({ps.Start:0.00}-{ps.End:0.00} spk{ps.Speaker})");
                }
            }
        }
        else
        {
            mismatches.Add($"count differs: c#={segs.Count} py={pySegs.Count}");
        }

        var outSegs = segs.Select(s => new Dictionary<string, object>
        {
            ["Speaker"] = s.speaker,
            ["Start"] = Math.Round(s.startF * 0.01, 2),
            ["End"] = Math.Round(s.endF * 0.01, 2)
        }).ToList();

        var result = new Dictionary<string, object>
        {
            ["onnx"] = onnx,
            ["num_steps"] = numSteps,
            ["overall_max_abs_diff"] = overallMax,
            ["steps"] = steps,
            ["segments_csharp_count"] = segs.Count,
            ["segments_python_count"] = pySegs.Count,
            ["segments_match"] = match,
            ["segment_mismatches"] = mismatches,
            ["segments_csharp"] = outSegs
        };
        File.WriteAllText(outJson, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("segments_match: " + match);
        if (!match) foreach (var m in mismatches) Console.WriteLine("  MISMATCH " + m);
        Console.WriteLine("results written to: " + outJson);
        Console.WriteLine("ORT RUNNER DONE");
        return 0;
    }

    static float[] ReadFloats(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var arr = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
        return arr;
    }
}
