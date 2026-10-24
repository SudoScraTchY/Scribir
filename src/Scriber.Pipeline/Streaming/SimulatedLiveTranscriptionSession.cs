using Scriber.Core.Audio;
using Scriber.Core.Domain;

namespace Scriber.Pipeline.Streaming;

/// <summary>
/// Deterministic simulated streaming session for testing and offline environments
/// when full AI weights have not yet been downloaded.
/// </summary>
public sealed class SimulatedLiveTranscriptionSession : ILiveTranscriptionSession
{
    private readonly TranscriptionPipelineOptions _options;
    private readonly List<LiveSegmentUpdate> _segments = new();
    private bool _isPaused;
    private bool _disposed;
    private TimeSpan _currentTime = TimeSpan.Zero;
    private int _sampleCount = 0;
    private int _phraseIndex = 0;

    private static readonly string[] SimulatedPhrases =
    [
        "Welcome to the meeting.",
        "Today we are reviewing the project architecture.",
        "All biometric voiceprints are kept strictly on-device.",
        "Performance on low-end hardware remains smooth.",
        "Let's proceed to the next item on our agenda."
    ];

    public event EventHandler<LiveSegmentUpdate>? SegmentUpdated;
    public IReadOnlyList<LiveSegmentUpdate> Segments => _segments;
    public bool IsPaused => _isPaused;

    public SimulatedLiveTranscriptionSession(TranscriptionPipelineOptions? options = null)
    {
        _options = options ?? new TranscriptionPipelineOptions();
    }

    public ValueTask PushAudioAsync(ReadOnlyMemory<float> samples, TimeSpan timestamp, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isPaused || samples.Length == 0) return ValueTask.CompletedTask;

        _sampleCount += samples.Length;
        _currentTime = timestamp;

        // Every ~2.5 seconds of audio (40,000 samples at 16kHz), simulate an incoming segment
        if (_sampleCount >= 40000)
        {
            _sampleCount = 0;
            string text = SimulatedPhrases[_phraseIndex % SimulatedPhrases.Length];
            string speakerId = $"speaker-{_phraseIndex % 2}";

            var segStart = _currentTime - TimeSpan.FromSeconds(2.0);
            if (segStart < TimeSpan.Zero) segStart = TimeSpan.Zero;
            var segEnd = _currentTime;

            var update = new LiveSegmentUpdate(speakerId, text, segStart, segEnd, IsFinal: true);
            _segments.Add(update);
            SegmentUpdated?.Invoke(this, update);

            _phraseIndex++;
        }

        return ValueTask.CompletedTask;
    }

    public void Pause() => _isPaused = true;
    public void Resume() => _isPaused = false;

    public ValueTask<Transcript> CompleteDraftAsync(string sourcePath, string? title = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var domainSegments = new List<Segment>(_segments.Count);
        var speakerIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var seg in _segments)
        {
            speakerIds.Add(seg.SpeakerId);
            var words = SynthesizeWords(seg.Text, seg.Start, seg.End);
            domainSegments.Add(new Segment(seg.SpeakerId, seg.Start, seg.End, words));
        }

        var speakers = speakerIds.Select((id, idx) =>
            new Speaker(id, $"Speaker {idx + 1}", idx % 2 == 0 ? "#4F46E5" : "#10B981")).ToList();

        if (speakers.Count == 0)
        {
            speakers.Add(new Speaker("speaker-0", "Speaker 1", "#4F46E5"));
        }

        string sessionTitle = !string.IsNullOrWhiteSpace(title)
            ? title
            : $"Live Recording ({DateTime.Now:MMM dd, HH:mm})";

        var audioRef = new AudioReference(
            localPath: sourcePath,
            duration: _currentTime > TimeSpan.Zero ? _currentTime : TimeSpan.FromSeconds(5.0),
            sampleRate: 16000);

        var draft = new Transcript(
            id: Guid.NewGuid(),
            title: sessionTitle,
            createdAt: DateTimeOffset.UtcNow,
            languageCode: _options.LanguageCode,
            speakers: speakers,
            segments: domainSegments,
            audio: audioRef);

        return ValueTask.FromResult(draft);
    }

    private static IReadOnlyList<Word> SynthesizeWords(string text, TimeSpan start, TimeSpan end)
    {
        var raw = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (raw.Length == 0) return [];

        double dur = (end - start).TotalSeconds;
        double perWord = dur / raw.Length;
        var words = new List<Word>(raw.Length);

        for (int i = 0; i < raw.Length; i++)
        {
            var wStart = start + TimeSpan.FromSeconds(i * perWord);
            var wEnd = (i == raw.Length - 1) ? end : start + TimeSpan.FromSeconds((i + 1) * perWord);
            if (wEnd <= wStart) wEnd = wStart + TimeSpan.FromMilliseconds(50);
            if (wEnd > end) wEnd = end;

            words.Add(new Word(raw[i], wStart, wEnd, 0.95));
        }

        return words;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
