using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using SherpaOnnx;
using Scriber.Core.Audio;
using Scriber.Core.Domain;

namespace Scriber.Inference.Asr;

/// <summary>
/// Offline automated speech recognition backend powered by Whisper (INT8) running through sherpa-onnx.
/// Implements ADR-0005.
/// </summary>
public sealed class SherpaWhisperAsrBackend : IAsrBackend, IDisposable
{
    public const int SampleRate = 16000;
    public const double MaxWindowSeconds = 28.0;

    private static readonly string[] WhisperLanguages =
    [
        "en", "fa", "zh", "de", "es", "ru", "ko", "fr", "ja", "pt",
        "tr", "pl", "ca", "nl", "ar", "sv", "it", "id", "hi", "fi",
        "vi", "he", "uk", "el", "ms", "cs", "ro", "da", "hu", "ta",
        "no", "th", "ur", "hr", "bg", "lt", "la", "mi", "ml", "cy",
        "sk", "te", "lv", "bn", "sr", "az", "sl", "kn", "et", "mk",
        "br", "eu", "is", "hy", "ne", "mn", "bs", "kk", "sq", "sw",
        "gl", "mr", "pa", "si", "km", "sn", "yo", "so", "af", "oc",
        "ka", "be", "tg", "sd", "gu", "am", "yi", "lo", "uz", "fo",
        "ht", "ps", "tk", "nn", "mt", "sa", "lb", "my", "bo", "tl",
        "mg", "as", "tt", "haw", "ln", "ha", "ba", "jw", "su"
    ];

    private readonly SherpaWhisperConfig _config;
    private readonly OfflineRecognizer _recognizer;
    private bool _disposed;

    public string Id => "sherpa-whisper-small-int8";
    public string DisplayName => "Whisper Small (INT8) via sherpa-onnx";
    public IReadOnlyList<string> SupportedLanguageCodes => WhisperLanguages;
    public bool SupportsStreaming => false;

    public SherpaWhisperAsrBackend(SherpaWhisperConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.EncoderPath, nameof(config.EncoderPath));
        ArgumentException.ThrowIfNullOrWhiteSpace(config.DecoderPath, nameof(config.DecoderPath));
        ArgumentException.ThrowIfNullOrWhiteSpace(config.TokensPath, nameof(config.TokensPath));

        if (!File.Exists(config.EncoderPath))
            throw new FileNotFoundException($"Whisper encoder ONNX model not found: {config.EncoderPath}", config.EncoderPath);
        if (!File.Exists(config.DecoderPath))
            throw new FileNotFoundException($"Whisper decoder ONNX model not found: {config.DecoderPath}", config.DecoderPath);
        if (!File.Exists(config.TokensPath))
            throw new FileNotFoundException($"Whisper tokens file not found: {config.TokensPath}", config.TokensPath);

        _config = config;

        var recognizerConfig = new OfflineRecognizerConfig();
        recognizerConfig.FeatConfig.SampleRate = SampleRate;
        recognizerConfig.ModelConfig.Whisper.Encoder = config.EncoderPath;
        recognizerConfig.ModelConfig.Whisper.Decoder = config.DecoderPath;
        recognizerConfig.ModelConfig.Whisper.Language = config.Language;
        recognizerConfig.ModelConfig.Whisper.Task = config.Task;
        recognizerConfig.ModelConfig.Whisper.EnableTokenTimestamps = 1;
        recognizerConfig.ModelConfig.Tokens = config.TokensPath;
        recognizerConfig.ModelConfig.NumThreads = config.NumThreads;
        recognizerConfig.ModelConfig.Provider = config.Provider;
        recognizerConfig.DecodingMethod = "greedy_search";

        _recognizer = new OfflineRecognizer(recognizerConfig);
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
        if (!SupportsStreaming)
        {
            throw new NotSupportedException($"Backend '{Id}' does not support live streaming transcription.");
        }

        yield break;
    }

    private TranscriptSegment[] TranscribeCore(float[] samples, AsrOptions options, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (samples.Length == 0)
            return [];

        ct.ThrowIfCancellationRequested();

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

            using var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(SampleRate, slice);
            _recognizer.Decode(stream);

            var r = stream.Result;
            if (string.IsNullOrWhiteSpace(r.Text))
                continue;

            TimeSpan windowStart = TimeSpan.FromSeconds(offset / (double)SampleRate);
            TimeSpan windowEnd = TimeSpan.FromSeconds((offset + len) / (double)SampleRate);

            var segment = BuildSegment(r, windowStart, windowEnd);
            if (segment != null)
            {
                segments.Add(segment);
            }
        }

        return segments.ToArray();
    }

    private static TranscriptSegment? BuildSegment(OfflineRecognizerResult r, TimeSpan windowStart, TimeSpan windowEnd)
    {
        var rawText = r.Text?.Trim();
        if (string.IsNullOrEmpty(rawText))
            return null;

        var words = new List<Word>();
        var tokens = r.Tokens;
        var timestamps = r.Timestamps;
        var durations = r.Durations;

        bool hasTimestamps = timestamps != null && timestamps.Length > 0 && timestamps.Any(t => t > 0.01f);

        if (tokens != null && tokens.Length > 0 && hasTimestamps)
        {
            // Build words from token stream
            var currentTokens = new List<string>();
            double wordStartSec = -1;
            double wordEndSec = -1;

            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (string.IsNullOrWhiteSpace(tok) || tok.StartsWith("<|"))
                    continue;

                double t = (timestamps != null && i < timestamps.Length) ? timestamps[i] : -1;
                double d = (durations != null && i < durations.Length) ? durations[i] : 0.25;
                if (d <= 0) d = 0.25;

                bool startsWord = tok.Length > 0 && (char.IsWhiteSpace(tok[0]) || tok.StartsWith("Ġ") || currentTokens.Count == 0);

                if (startsWord && currentTokens.Count > 0)
                {
                    EmitWord(words, currentTokens, wordStartSec, wordEndSec, windowStart, windowEnd);
                    currentTokens.Clear();
                    wordStartSec = -1;
                }

                currentTokens.Add(tok);
                if (wordStartSec < 0 && t >= 0) wordStartSec = t;
                if (t >= 0) wordEndSec = t + d;
            }

            if (currentTokens.Count > 0)
            {
                EmitWord(words, currentTokens, wordStartSec, wordEndSec, windowStart, windowEnd);
            }
        }

        // If no token timestamps were available, interpolate words linearly
        if (words.Count == 0)
        {
            var rawWords = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rawWords.Length > 0)
            {
                double totalSeconds = (windowEnd - windowStart).TotalSeconds;
                double perWordSec = Math.Max(0.1, totalSeconds / rawWords.Length);
                for (int i = 0; i < rawWords.Length; i++)
                {
                    var wStart = windowStart + TimeSpan.FromSeconds(i * perWordSec);
                    var wEnd = windowStart + TimeSpan.FromSeconds((i + 1) * perWordSec);
                    if (wEnd > windowEnd) wEnd = windowEnd;
                    if (wEnd <= wStart) wEnd = wStart + TimeSpan.FromMilliseconds(50);
                    words.Add(new Word(rawWords[i], wStart, wEnd, 0.90));
                }
            }
        }

        var segStart = words.Count > 0 ? words[0].Start : windowStart;
        var segEnd = words.Count > 0 ? words[^1].End : windowEnd;
        if (segEnd <= segStart) segEnd = segStart + TimeSpan.FromMilliseconds(100);

        return new TranscriptSegment(rawText, segStart, segEnd, words);
    }

    private static void EmitWord(List<Word> words, List<string> tokens, double startSec, double endSec, TimeSpan winStart, TimeSpan winEnd)
    {
        string raw = string.Concat(tokens).Trim().Replace("Ġ", "");
        raw = Regex.Replace(raw, @"\s+", " ").Trim();
        if (string.IsNullOrEmpty(raw))
            return;

        TimeSpan wStart = startSec >= 0 ? winStart + TimeSpan.FromSeconds(startSec) : winStart;
        TimeSpan wEnd = endSec >= 0 ? winStart + TimeSpan.FromSeconds(endSec) : wStart + TimeSpan.FromMilliseconds(200);

        if (wEnd <= wStart)
            wEnd = wStart + TimeSpan.FromMilliseconds(100);
        if (wEnd > winEnd)
            wEnd = winEnd;

        // Ensure chronological monotonicity
        if (words.Count > 0 && wStart < words[^1].End)
        {
            wStart = words[^1].End;
            if (wEnd <= wStart)
                wEnd = wStart + TimeSpan.FromMilliseconds(50);
        }

        words.Add(new Word(raw, wStart, wEnd, 0.90));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _recognizer.Dispose();
            _disposed = true;
        }
    }
}
