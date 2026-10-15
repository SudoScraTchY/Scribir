using SherpaOnnx;
using Scriber.Core.Audio;

namespace Scriber.Pipeline.Vad;

/// <summary>
/// IVad backed by sherpa-onnx's Silero VAD (PH0-SPIKE-002 validated stack).
/// </summary>
public sealed class SherpaVad : IVad
{
    public const int SampleRate = 16000;

    private readonly string _modelPath;
    private readonly VadOptions _options;

    /// <param name="modelPath">Path to a silero VAD ONNX model (sherpa v5 signature:
    /// input/state/sr → output/stateN).</param>
    public SherpaVad(string modelPath, VadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath, nameof(modelPath));
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Silero VAD model not found: {modelPath}", modelPath);
        }

        _modelPath = modelPath;
        _options = options ?? new VadOptions();
    }

    public IReadOnlyList<SpeechRange> DetectSpeech(AudioBuffer audio, CancellationToken ct = default)
    {
        if (audio.SampleRate != SampleRate)
        {
            throw new ArgumentException($"VAD requires {SampleRate} Hz PCM, got {audio.SampleRate}.", nameof(audio));
        }
        if (audio.Channels != 1)
        {
            throw new ArgumentException($"VAD requires mono PCM, got {audio.Channels} channels.", nameof(audio));
        }

        var config = new VadModelConfig
        {
            SampleRate = SampleRate,
            NumThreads = 1,
            Provider = "cpu",
            SileroVad = new SileroVadModelConfig
            {
                Model = _modelPath,
                Threshold = _options.Threshold,
                MinSilenceDuration = _options.MinSilenceDuration,
                MinSpeechDuration = _options.MinSpeechDuration,
                WindowSize = _options.WindowSize,
                MaxSpeechDuration = _options.MaxSpeechDuration,
            },
        };

        using var vad = new VoiceActivityDetector(config, bufferSizeInSeconds: 30f);

        var ranges = new List<SpeechRange>();
        // Feed in WindowSize chunks (512 samples = 32 ms) so sherpa-onnx evaluates
        // each window independently without or-ing speech across large buffers.
        var chunkSamples = _options.WindowSize;
        for (var offset = 0; offset < audio.Samples.Length; offset += chunkSamples)
        {
            var length = Math.Min(chunkSamples, audio.Samples.Length - offset);
            vad.AcceptWaveform(audio.Samples.AsSpan(offset, length).ToArray());
            Drain(vad, ranges);
            ct.ThrowIfCancellationRequested();
        }

        vad.Flush(); // emit any trailing speech not yet closed by silence
        Drain(vad, ranges);

        return ranges;
    }

    private static void Drain(VoiceActivityDetector vad, List<SpeechRange> ranges)
    {
        while (!vad.IsEmpty())
        {
            var segment = vad.Front();
            vad.Pop();
            var start = TimeSpan.FromSeconds(segment.Start / (double)SampleRate);
            var end = TimeSpan.FromSeconds((segment.Start + segment.Samples.Length) / (double)SampleRate);
            ranges.Add(new SpeechRange(start, end));
        }
    }
}
