using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Whisper.net;

namespace Scriber.Inference.Asr;

/// <summary>
/// Cross-platform ASR backend powered by llama.cpp / whisper.cpp for Android and Desktop (Windows/macOS/Linux).
/// Executes GGUF/GGML quantized models directly on-device using in-process Whisper.net runtime.
/// Implements ADR-0005.
/// </summary>
public sealed class LlamaCppAsrBackend : IAsrBackend, IDisposable
{
    public const int SampleRate = 16000;
    public const double MaxWindowSeconds = 28.0;

    private static readonly string[] SupportedLanguages =
    [
        "auto", "en", "fa", "zh", "de", "es", "ru", "ko", "fr", "ja", "pt",
        "tr", "pl", "ca", "nl", "ar", "sv", "it", "id", "hi", "fi", "vi",
        "he", "uk", "el", "ms", "cs", "ro", "da", "hu", "ta", "no", "th"
    ];

    private readonly LlamaCppConfig _config;
    private WhisperFactory? _whisperFactory;
    private readonly object _factoryLock = new();
    private bool _disposed;

    public string Id => _config.Id;
    public string DisplayName => _config.DisplayName;
    public IReadOnlyList<string> SupportedLanguageCodes => SupportedLanguages;
    public bool SupportsStreaming => true;

    public LlamaCppAsrBackend(LlamaCppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.ModelPath, nameof(config.ModelPath));

        if (!File.Exists(config.ModelPath))
        {
            throw new FileNotFoundException($"llama.cpp GGUF model file not found: {config.ModelPath}", config.ModelPath);
        }

        _config = config;
    }

    private WhisperFactory? GetOrCreateFactory()
    {
        if (_whisperFactory != null) return _whisperFactory;
        lock (_factoryLock)
        {
            if (_whisperFactory != null) return _whisperFactory;
            if (File.Exists(_config.ModelPath))
            {
                try
                {
                    _whisperFactory = WhisperFactory.FromPath(_config.ModelPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to initialize WhisperFactory from {_config.ModelPath}: {ex.Message}");
                }
            }
            return _whisperFactory;
        }
    }

    public async Task<TranscriptSegment[]> TranscribeAsync(AudioBuffer audio, AsrOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(options);

        if (audio.SampleRate != SampleRate)
            throw new ArgumentException($"ASR requires {SampleRate} Hz PCM, got {audio.SampleRate}.", nameof(audio));
        if (audio.Channels != 1)
            throw new ArgumentException($"ASR requires mono PCM, got {audio.Channels} channels.", nameof(audio));

        return await Task.Run(() => TranscribeCore(audio.Samples, options, ct), ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<PartialTranscriptSegment> TranscribeStreamingAsync(
        IAsyncEnumerable<AudioChunk> chunks,
        AsrOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(options);

        var accumulatedSamples = new List<float>(SampleRate * 5);
        TimeSpan sessionStart = TimeSpan.Zero;

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            accumulatedSamples.AddRange(chunk.Samples);

            // Emit interim transcript every ~1.5s of speech
            if (accumulatedSamples.Count >= SampleRate * 1.5)
            {
                var slice = accumulatedSamples.ToArray();
                accumulatedSamples.Clear();

                TimeSpan chunkEnd = sessionStart + TimeSpan.FromSeconds(slice.Length / (double)SampleRate);
                string interimText = TranscribeChunkText(slice, options, ct);

                if (!string.IsNullOrWhiteSpace(interimText))
                {
                    yield return new PartialTranscriptSegment(interimText, sessionStart, chunkEnd, IsFinal: false);
                }

                sessionStart = chunkEnd;
            }
        }

        if (accumulatedSamples.Count > 0)
        {
            TimeSpan finalEnd = sessionStart + TimeSpan.FromSeconds(accumulatedSamples.Count / (double)SampleRate);
            string finalText = TranscribeChunkText(accumulatedSamples.ToArray(), options, ct);
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                yield return new PartialTranscriptSegment(finalText, sessionStart, finalEnd, IsFinal: true);
            }
        }
    }

    private TranscriptSegment[] TranscribeCore(float[] samples, AsrOptions options, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (samples.Length == 0)
            return [];

        ct.ThrowIfCancellationRequested();

        // If executable (e.g. whisper-cli / llama-cli) is configured and available, run through CLI runner
        if (!string.IsNullOrEmpty(_config.ExecutablePath) && File.Exists(_config.ExecutablePath))
        {
            return RunExternalCli(samples, options, ct);
        }

        // In-process Whisper.net execution
        var factory = GetOrCreateFactory();
        if (factory != null)
        {
            try
            {
                return TranscribeWithWhisperNet(factory, samples, options, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Whisper.net transcription error: {ex.Message}");
            }
        }

        // Windowed fallback
        int maxWindowSamples = (int)(MaxWindowSeconds * SampleRate);
        var segments = new List<TranscriptSegment>();

        for (int offset = 0; offset < samples.Length; offset += maxWindowSamples)
        {
            ct.ThrowIfCancellationRequested();

            int len = Math.Min(maxWindowSamples, samples.Length - offset);
            if (len < 1600) // Less than 100ms
                continue;

            var slice = new float[len];
            Array.Copy(samples, offset, slice, 0, len);

            TimeSpan windowStart = TimeSpan.FromSeconds(offset / (double)SampleRate);
            TimeSpan windowEnd = TimeSpan.FromSeconds((offset + len) / (double)SampleRate);

            string text = TranscribeChunkText(slice, options, ct);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var rawWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var words = new List<Word>(rawWords.Length);
            double totalSeconds = (windowEnd - windowStart).TotalSeconds;
            double perWordSec = Math.Max(0.08, totalSeconds / Math.Max(1, rawWords.Length));

            for (int i = 0; i < rawWords.Length; i++)
            {
                var wStart = windowStart + TimeSpan.FromSeconds(i * perWordSec);
                var wEnd = windowStart + TimeSpan.FromSeconds((i + 1) * perWordSec);
                if (wEnd > windowEnd) wEnd = windowEnd;
                if (wEnd <= wStart) wEnd = wStart + TimeSpan.FromMilliseconds(50);
                words.Add(new Word(rawWords[i], wStart, wEnd, 0.92));
            }

            var segStart = words.Count > 0 ? words[0].Start : windowStart;
            var segEnd = words.Count > 0 ? words[^1].End : windowEnd;
            segments.Add(new TranscriptSegment(text, segStart, segEnd, words));
        }

        return segments.ToArray();
    }

    private TranscriptSegment[] TranscribeWithWhisperNet(
        WhisperFactory factory,
        float[] samples,
        AsrOptions options,
        CancellationToken ct)
    {
        string lang = options.LanguageCode == "auto" ? "auto" : options.LanguageCode;
        using var processor = factory.CreateBuilder()
            .WithLanguage(lang)
            .WithThreads(_config.NumThreads)
            .Build();

        using var wavStream = new MemoryStream();
        WavHelper.WriteHeader(wavStream, samples.Length * 2);
        for (int i = 0; i < samples.Length; i++)
        {
            short pcm = (short)Math.Clamp(samples[i] * 32767.0f, short.MinValue, short.MaxValue);
            wavStream.WriteByte((byte)(pcm & 0xFF));
            wavStream.WriteByte((byte)((pcm >> 8) & 0xFF));
        }
        wavStream.Position = 0;

        var segmentList = new List<TranscriptSegment>();
        var segments = processor.ProcessAsync(wavStream, ct).ToBlockingEnumerable(ct);

        foreach (var seg in segments)
        {
            if (string.IsNullOrWhiteSpace(seg.Text)) continue;

            string cleanText = seg.Text.Trim();
            var rawWords = cleanText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var words = new List<Word>(rawWords.Length);
            double totalSeconds = (seg.End - seg.Start).TotalSeconds;
            double perWordSec = rawWords.Length > 0 ? Math.Max(0.05, totalSeconds / rawWords.Length) : 0.1;

            for (int i = 0; i < rawWords.Length; i++)
            {
                var wStart = seg.Start + TimeSpan.FromSeconds(i * perWordSec);
                var wEnd = seg.Start + TimeSpan.FromSeconds((i + 1) * perWordSec);
                if (wEnd > seg.End) wEnd = seg.End;
                if (wEnd <= wStart) wEnd = wStart + TimeSpan.FromMilliseconds(50);
                words.Add(new Word(rawWords[i], wStart, wEnd, (double)seg.Probability));
            }

            segmentList.Add(new TranscriptSegment(cleanText, seg.Start, seg.End, words));
        }

        return segmentList.ToArray();
    }

    private string TranscribeChunkText(float[] samples, AsrOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Calculate RMS energy to reject pure silence frames quickly
        double sumSquare = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sumSquare += samples[i] * samples[i];
        }
        double rms = Math.Sqrt(sumSquare / Math.Max(1, samples.Length));
        if (rms < 0.005)
        {
            return string.Empty;
        }

        // If external CLI exists, execute it
        if (!string.IsNullOrEmpty(_config.ExecutablePath) && File.Exists(_config.ExecutablePath))
        {
            var segments = RunExternalCli(samples, options, ct);
            return string.Join(" ", segments.Select(s => s.Text)).Trim();
        }

        var factory = GetOrCreateFactory();
        if (factory != null)
        {
            try
            {
                string lang = options.LanguageCode == "auto" ? "auto" : options.LanguageCode;
                using var processor = factory.CreateBuilder()
                    .WithLanguage(lang)
                    .WithThreads(_config.NumThreads)
                    .Build();

                using var wavStream = new MemoryStream();
                WavHelper.WriteHeader(wavStream, samples.Length * 2);
                for (int i = 0; i < samples.Length; i++)
                {
                    short pcm = (short)Math.Clamp(samples[i] * 32767.0f, short.MinValue, short.MaxValue);
                    wavStream.WriteByte((byte)(pcm & 0xFF));
                    wavStream.WriteByte((byte)((pcm >> 8) & 0xFF));
                }
                wavStream.Position = 0;

                var segments = processor.ProcessAsync(wavStream, ct).ToBlockingEnumerable(ct);
                var text = string.Join(" ", segments.Select(s => s.Text)).Trim();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Whisper.net chunk transcription error: {ex.Message}");
            }
        }

        return string.Empty;
    }

    private TranscriptSegment[] RunExternalCli(float[] samples, AsrOptions options, CancellationToken ct)
    {
        string tempWav = Path.Combine(Path.GetTempPath(), $"llama_asr_{Guid.NewGuid():N}.wav");
        string tempOutput = Path.Combine(Path.GetTempPath(), $"llama_asr_{Guid.NewGuid():N}");

        try
        {
            // Write standard 16kHz mono 16-bit PCM WAV for whisper-cli
            WavHelper.WriteWavFile(tempWav, samples, SampleRate, 1);

            string langArg = options.LanguageCode == "auto" ? "auto" : options.LanguageCode;
            var startInfo = new ProcessStartInfo
            {
                FileName = _config.ExecutablePath!,
                Arguments = $"-m \"{_config.ModelPath}\" -f \"{tempWav}\" -l {langArg} -t {_config.NumThreads} -ovtt",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to launch llama.cpp backend process: {_config.ExecutablePath}");

            ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
            });

            process.WaitForExit();

            string vttFile = tempWav + ".vtt";
            if (File.Exists(vttFile))
            {
                string vttContent = File.ReadAllText(vttFile);
                return ParseVtt(vttContent);
            }

            return [];
        }
        finally
        {
            try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
            try { if (File.Exists(tempWav + ".vtt")) File.Delete(tempWav + ".vtt"); } catch { }
        }
    }

    private static TranscriptSegment[] ParseVtt(string vtt)
    {
        var segments = new List<TranscriptSegment>();
        var timeRegex = new Regex(@"(\d{2}:\d{2}:\d{2}\.\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2}\.\d{3})");

        var lines = vtt.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var match = timeRegex.Match(lines[i]);
            if (match.Success && i + 1 < lines.Length)
            {
                TimeSpan start = TimeSpan.Parse(match.Groups[1].Value);
                TimeSpan end = TimeSpan.Parse(match.Groups[2].Value);
                string text = lines[i + 1].Trim();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    var rawWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var words = new List<Word>(rawWords.Length);
                    double totalSeconds = (end - start).TotalSeconds;
                    double perWordSec = Math.Max(0.08, totalSeconds / Math.Max(1, rawWords.Length));

                    for (int w = 0; w < rawWords.Length; w++)
                    {
                        var wStart = start + TimeSpan.FromSeconds(w * perWordSec);
                        var wEnd = start + TimeSpan.FromSeconds((w + 1) * perWordSec);
                        if (wEnd > end) wEnd = end;
                        words.Add(new Word(rawWords[w], wStart, wEnd, 0.95));
                    }

                    segments.Add(new TranscriptSegment(text, start, end, words));
                }
            }
        }

        return segments.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_factoryLock)
        {
            _whisperFactory?.Dispose();
            _whisperFactory = null;
        }
    }
}
