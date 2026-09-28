// PH0-SPIKE-001 — sherpa-onnx C# diarization API probe.
//
// Modes:
//   all        run everything (reflection + control + experiment)
//   control    reflection + native pyannote+3dspeaker control run only
//   exp        the experiment: use OUR exported Nemotron step graph as the
//              "pyannote" segmentation model (run last; may crash natively)
//
// Usage:  SherpaAttempt <mode> <fixture.wav> <sherpa-models-dir> <custom.onnx>

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SherpaOnnx;

class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string mode = args.Length > 0 ? args[0] : "all";
        string fixtureWav = args.Length > 1 ? args[1] : @"M:\Agentic\Projects\Scribir\spikes\PH0-SPIKE-001\artifacts\audio\two_speaker_fixture.wav";
        string sherpaDir = args.Length > 2 ? args[2] : @"M:\Agentic\Projects\Scribir\spikes\PH0-SPIKE-001\artifacts\sherpa-models";
        string customOnnx = args.Length > 3 ? args[3] : @"M:\Agentic\Projects\Scribir\spikes\PH0-SPIKE-001\artifacts\onnx\nemotron3_stream_step.onnx";

        var asm = typeof(OfflineSpeakerDiarization).Assembly;
        Console.WriteLine($"SherpaOnnx assembly: {asm.GetName().Name} v{asm.GetName().Version}");

        if (mode == "all" || mode == "control" || mode == "exp")
        {
            Console.WriteLine("=== [1] Diarization-related public types in the C# binding ===");
            foreach (var t in asm.GetTypes()
                         .Where(t => t.IsPublic && t.Name.IndexOf("Diar", StringComparison.OrdinalIgnoreCase) >= 0)
                         .OrderBy(t => t.FullName))
                Console.WriteLine("  " + t.FullName);

            Console.WriteLine("=== [2] Any streaming/online diarization types? ===");
            var streamDiar = asm.GetTypes()
                .Where(t => (t.Name.Contains("Online") || t.Name.Contains("Stream")) &&
                            t.Name.IndexOf("Diar", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            if (streamDiar.Count == 0)
                Console.WriteLine("  none: no streaming/online diarization API exists in sherpa-onnx v1.13.8 C# bindings");
            else
                foreach (var t in streamDiar) Console.WriteLine("  " + t.FullName);

            Console.WriteLine("=== [3] OfflineSpeakerDiarization public members ===");
            foreach (var m in typeof(OfflineSpeakerDiarization)
                         .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .OrderBy(m => m.Name))
                Console.WriteLine("  " + m.MemberType + ": " + m);
        }

        if (mode == "all" || mode == "control")
        {
            Console.WriteLine("=== [4] CONTROL — native sherpa models (pyannote segmentation + 3dspeaker embedding) ===");
            try
            {
                var config = new OfflineSpeakerDiarizationConfig();
                config.Segmentation.Pyannote.Model = Path.Combine(sherpaDir, "sherpa-onnx-pyannote-segmentation-3-0", "model.onnx");
                config.Embedding.Model = Path.Combine(sherpaDir, "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx");
                config.Clustering.NumClusters = 2;
                Console.WriteLine("  constructing OfflineSpeakerDiarization...");
                using var sd = new OfflineSpeakerDiarization(config);
                Console.WriteLine("  SampleRate: " + sd.SampleRate);
                var wave = new WaveReader(fixtureWav);
                Console.WriteLine($"  wave: {wave.SampleRate} Hz, {wave.Samples.Length} samples, {wave.Samples.Length / (double)wave.SampleRate:0.00}s");
                Console.WriteLine("  processing...");
                var segments = sd.Process(wave.Samples);
                Console.WriteLine($"  segments: {segments.Length}");
                foreach (var s in segments)
                    Console.WriteLine($"    {s.Start:0.00} -- {s.End:0.00} speaker_{s.Speaker}");
            }
            catch (Exception e)
            {
                Console.WriteLine("  EXCEPTION: " + e.GetType().FullName + ": " + e.Message);
            }
        }

        if (mode == "all" || mode == "exp")
        {
            Console.WriteLine("=== [5] EXPERIMENT — our exported Nemotron step graph as the pyannote segmentation model ===");
            Console.WriteLine("  (sherpa's only pluggable segmentation slot; expected to fail metadata validation)");
            try
            {
                var config = new OfflineSpeakerDiarizationConfig();
                config.Segmentation.Pyannote.Model = customOnnx;
                config.Embedding.Model = Path.Combine(sherpaDir, "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx");
                config.Clustering.NumClusters = 2;
                Console.WriteLine("  constructing with custom segmentation model: " + customOnnx);
                using var sd = new OfflineSpeakerDiarization(config);
                Console.WriteLine("  constructed (unexpected); SampleRate: " + sd.SampleRate);
                var wave = new WaveReader(fixtureWav);
                var segments = sd.Process(wave.Samples);
                Console.WriteLine($"  segments: {segments.Length}");
                foreach (var s in segments)
                    Console.WriteLine($"    {s.Start:0.00} -- {s.End:0.00} speaker_{s.Speaker}");
            }
            catch (Exception e)
            {
                Console.WriteLine("  EXCEPTION: " + e.GetType().FullName + ": " + e.Message);
            }
        }

        Console.WriteLine("SHERPA ATTEMPT DONE");
    }
}
