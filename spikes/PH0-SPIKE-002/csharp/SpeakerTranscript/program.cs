using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiarizerPort;
using SherpaOnnx;

namespace SpeakerTranscript;

/// <summary>
/// PH0-SPIKE-002 console: diarize (raw ONNX Runtime, per PH0-SPIKE-001) -> ASR (sherpa-onnx
/// Whisper small int8, multilingual) -> naive overlap alignment -> speaker-labeled transcript.
/// </summary>
public static class Program
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("repo root (.git) not found");
        return dir.FullName;
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string root = RepoRoot();
        string assets = Path.Combine(root, "spikes", "PH0-SPIKE-001", "artifacts", "csharp-port-assets");
        string onnx = Path.Combine(root, "spikes", "PH0-SPIKE-001", "artifacts", "onnx", "nemotron3_stream_step.onnx");
        string whisperDir = Path.Combine(root, "spikes", "PH0-SPIKE-002", "artifacts", "whisper", "sherpa-onnx-whisper-small");
        string runsDir = Path.Combine(root, "spikes", "PH0-SPIKE-002", "csharp", "SpeakerTranscript", "runs");

        var ortDll = Path.Combine(AppContext.BaseDirectory, "onnxruntime.dll");
        Console.WriteLine($"onnxruntime.dll next to exe: {(File.Exists(ortDll) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(ortDll).FileVersion : "MISSING")}");

        if (args.Length == 0) { Console.WriteLine("usage: SpeakerTranscript --audio <wav> [--out dir] | --smoke"); return 2; }

        string whisperEnc = Path.Combine(whisperDir, "small-encoder.int8.onnx");
        string whisperDec = Path.Combine(whisperDir, "small-decoder.int8.onnx");
        string whisperTok = Path.Combine(whisperDir, "small-tokens.txt");

        if (args[0] == "--smoke")
        {
            Console.WriteLine("smoke: creating sherpa recognizer...");
            using var rec = CreateRecognizer(whisperEnc, whisperDec, whisperTok);
            Console.WriteLine("smoke: creating diarizer + one graph step...");
            using var d = new Diarizer(assets, onnx);
            var feats = new float[104 * 128];
            var cached = new float[0];
            _ = d.RunGraph(To2D(feats, 104, 128), 104, cached, 0);
            Console.WriteLine("SMOKE OK: both native stacks coexist");
            return 0;
        }

        if (args[0] != "--audio") { Console.WriteLine("unknown mode"); return 2; }
        string wav = args[1];
        string outDir = null;
        string wdir = whisperDir;
        for (int i = 2; i + 1 < args.Length; i += 2)
        {
            if (args[i] == "--out") outDir = args[i + 1];
            else if (args[i] == "--whisper-dir") wdir = args[i + 1];
        }
        outDir ??= Path.Combine(runsDir, Path.GetFileNameWithoutExtension(wav));
        whisperEnc = Path.Combine(wdir, "small-encoder.int8.onnx");
        whisperDec = Path.Combine(wdir, "small-decoder.int8.onnx");
        whisperTok = Path.Combine(wdir, "small-tokens.txt");
        Console.WriteLine("whisper dir: " + wdir);
        Directory.CreateDirectory(outDir);

        var (samples, sr) = WavReader.Read(wav);
        if (sr != 16000) throw new InvalidOperationException($"expected 16 kHz, got {sr}");
        Console.WriteLine($"audio: {wav} ({samples.Length / 16000.0:0.00}s)");

        // ---------- 1) diarization ----------
        List<Segment> diar;
        long diarMs;
        using (var d = new Diarizer(assets, onnx))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = d.Run(samples);
            sw.Stop();
            diar = res.Segments;
            diarMs = sw.ElapsedMilliseconds;
            Console.WriteLine($"diarization: {diar.Count} segments in {diarMs} ms");
        }

        // ---------- 2) ASR (sherpa-onnx Whisper small int8, multilingual) ----------
        double duration = samples.Length / 16000.0;
        using var recognizer = CreateRecognizer(whisperEnc, whisperDec, whisperTok);
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        List<(int speaker, double start, double end, string text)> aligned;
        bool haveTs = false;
        int tokCount = 0;

        if (duration <= 28.0)
        {
            Console.WriteLine("asr: whole-file decode with sherpa-onnx Whisper small int8 ...");
            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(16000, samples);
            recognizer.Decode(stream);
            var r = stream.Result;
            tokCount = r.Tokens?.Length ?? 0;
            if (Array.IndexOf(args, "--dump-tokens") >= 0)
            {
                for (int i = 0; i < (r.Tokens?.Length ?? 0); i++)
                {
                    double ts = (r.Timestamps != null && i < r.Timestamps.Length) ? r.Timestamps[i] : -1;
                    double du = (r.Durations != null && i < r.Durations.Length) ? r.Durations[i] : -1;
                    Console.WriteLine($"tok[{i,3}] ts={ts:0.000} dur={du:0.000} '{r.Tokens[i]}'");
                }
            }
            var utterances = BuildUtterances(r);
            haveTs = utterances.Count > 0 && utterances[^1].End > 0.01;
            Console.WriteLine($"asr: {tokCount} tokens; token timestamps {(haveTs ? "available" : "unavailable (stock decoder export lacks cross-attention outputs)")}");
            if (haveTs)
            {
                aligned = utterances.Select(u =>
                {
                    int spk = SpeakerFor(Math.Max(0, u.Start), Math.Max(u.Start + 0.01, u.End), diar);
                    return (spk, u.Start, u.End, u.Text);
                }).ToList();
            }
            else
            {
                aligned = PerTurnDecode(recognizer, samples, diar, ref tokCount);
            }
        }
        else
        {
            Console.WriteLine($"asr: audio {duration:0.0}s exceeds the sherpa offline Whisper window (<30 s) -> per-turn transcription");
            aligned = PerTurnDecode(recognizer, samples, diar, ref tokCount);
        }
        sw2.Stop();
        Console.WriteLine($"asr: done in {sw2.ElapsedMilliseconds} ms; {tokCount} tokens");

        // ---------- 4) output ----------
        Console.WriteLine();
        Console.WriteLine("=== transcript ===");
        var sb = new StringBuilder();
        foreach (var (spk, start, end, text) in aligned)
        {
            string line = $"[Speaker {spk}] [{Fmt(start)} - {Fmt(end)}] {text}";
            Console.WriteLine(line);
            sb.AppendLine(line);
        }

        File.WriteAllText(Path.Combine(outDir, "transcript.txt"), sb.ToString(), new UTF8Encoding(false));
        var json = new
        {
            audio = wav,
            sampleRate = sr,
            diarizationMs = diarMs,
            asrMs = sw2.ElapsedMilliseconds,
            usedTokenTimestamps = haveTs,
            asrTokens = tokCount,
            diarization = diar.Select(s => new { start = s.Start, end = s.End, speaker = s.Speaker }),
            utterances = aligned.Select(u => new { speaker = u.speaker, start = u.start, end = u.end, text = u.text }),
        };
        File.WriteAllText(Path.Combine(outDir, "transcript.json"),
            JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine();
        Console.WriteLine($"wrote: {Path.Combine(outDir, "transcript.txt")} and transcript.json");
        return 0;
    }

    /// <summary>Transcribe each diarization turn (turns split into &lt;=28 s sub-cuts to respect the
    /// offline Whisper window); naive cut-based alignment of ASR text to speaker turns.</summary>
    private static List<(int speaker, double start, double end, string text)> PerTurnDecode(
        OfflineRecognizer recognizer, float[] samples, List<Segment> diar, ref int tokCount)
    {
        var aligned = new List<(int, double, double, string)>();
        const double maxCut = 28.0;
        foreach (var seg in diar)
        {
            double segLen = seg.End - seg.Start;
            if (segLen <= 0) continue;
            int parts = Math.Max(1, (int)Math.Ceiling(segLen / maxCut));
            var sb = new StringBuilder();
            for (int p = 0; p < parts; p++)
            {
                double s0 = seg.Start + segLen * p / parts;
                double s1 = seg.Start + segLen * (p + 1) / parts;
                int a = (int)(s0 * 16000), b = Math.Min(samples.Length, (int)(s1 * 16000));
                if (b - a < 1600) continue;
                var cut = new float[b - a];
                Array.Copy(samples, a, cut, 0, cut.Length);
                using var st = recognizer.CreateStream();
                st.AcceptWaveform(16000, cut);
                recognizer.Decode(st);
                var rr = st.Result;
                tokCount += rr.Tokens?.Length ?? 0;
                var t = rr.Text?.Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(t);
                }
            }
            var text = sb.ToString();
            if (text.Length > 0) aligned.Add((seg.Speaker, seg.Start, seg.End, text));
        }
        return aligned;
    }

    private static OfflineRecognizer CreateRecognizer(string enc, string dec, string tok)
    {
        var cfg = new OfflineRecognizerConfig();
        cfg.FeatConfig.SampleRate = 16000;
        cfg.ModelConfig.Whisper.Encoder = enc;
        cfg.ModelConfig.Whisper.Decoder = dec;
        cfg.ModelConfig.Whisper.Language = "en";
        cfg.ModelConfig.Whisper.Task = "transcribe";
        cfg.ModelConfig.Whisper.EnableTokenTimestamps = 1;
        cfg.ModelConfig.Tokens = tok;
        cfg.ModelConfig.NumThreads = Math.Max(2, Environment.ProcessorCount / 2);
        cfg.ModelConfig.Provider = "cpu";
        cfg.DecodingMethod = "greedy_search";
        return new OfflineRecognizer(cfg);
    }

    private sealed record Utt(double Start, double End, string Text);

    /// <summary>Group whisper tokens into utterances: split before a new word-start token when the
    /// gap from the previous token is &gt; 0.35 s (word fragments and punctuation never start one;
    /// a degenerate leading timestamp of 0 is ignored). Ends are approximated (+0.3 s).</summary>
    private static List<Utt> BuildUtterances(OfflineRecognizerResult r)
    {
        var list = new List<Utt>();
        if (r.Tokens == null) return list;
        var ts = r.Timestamps;
        var sb = new StringBuilder();
        double curStart = 0, lastTs = 0;
        bool open = false;
        bool lastEndPunct = false;

        void Close()
        {
            if (!open) return;
            var text = Cleanup(sb.ToString());
            if (text.Length > 0) list.Add(new Utt(curStart, lastTs + 0.3, text));
            sb.Clear();
            open = false;
        }

        for (int i = 0; i < r.Tokens.Length; i++)
        {
            string raw = r.Tokens[i];
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("<|")) continue;
            double t = (ts != null && i < ts.Length) ? ts[i] : -1;
            bool hasLeadSpace = raw.Length > 0 && char.IsWhiteSpace(raw[0]);
            bool punct = IsPunctOnly(raw);
            if (!open) { open = true; curStart = Math.Max(t, 0); }
            else if (hasLeadSpace && !punct && t > 0.01 && lastTs > 0.01)
            {
                double gap = t - lastTs;
                // split after a sentence end (short pause) or at a long pause
                if ((lastEndPunct && gap > 0.25) || gap > 1.0)
                {
                    Close();
                    open = true;
                    curStart = t;
                }
            }
            sb.Append(raw);
            if (t >= 0) lastTs = t;
            lastEndPunct = IsSentenceEnd(raw);
        }
        Close();
        return list;
    }

    private static bool IsSentenceEnd(string s)
    {
        var t = s.TrimEnd();
        if (t.Length == 0) return false;
        return ".!?".Contains(t[^1]) || t.EndsWith("\u2026");
    }

    private static string Cleanup(string s)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"\s+([\.,!\?;:\u2026])", "$1");
        return s;
    }

    private static bool IsPunctOnly(string s)
    {
        var t = s.Trim();
        if (t.Length == 0) return true;
        return t.All(c => ".,!?;:\u2026-\u2013\u2014'\"".Contains(c));
    }

    private static int SpeakerFor(double start, double end, List<Segment> diar)
    {
        int best = -1; double bestOv = 0;
        foreach (var s in diar)
        {
            double ov = Math.Min(end, s.End) - Math.Max(start, s.Start);
            if (ov > bestOv) { bestOv = ov; best = s.Speaker; }
        }
        if (best >= 0) return best;
        // nearest by midpoint
        double mid = (start + end) / 2, bestD = double.MaxValue; int spk = 0;
        foreach (var s in diar)
        {
            double d2 = Math.Abs(mid - (s.Start + s.End) / 2);
            if (d2 < bestD) { bestD = d2; spk = s.Speaker; }
        }
        return spk;
    }

    private static string Fmt(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.Hours > 0 ? $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}"
                           : $"{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }

    private static float[,] To2D(float[] flat, int frames, int cols)
    {
        var m = new float[frames, cols];
        for (int i = 0; i < frames; i++)
            for (int j = 0; j < cols; j++)
                m[i, j] = flat[i * cols + j];
        return m;
    }
}
