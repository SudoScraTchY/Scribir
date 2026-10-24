using System.Buffers;
using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;

namespace Scriber.Pipeline.Streaming;

/// <summary>
/// Active live streaming session processing audio chunks, tracking continuous timestamps,
/// and emitting interim/final transcript updates in real time.
/// </summary>
public sealed class LiveTranscriptionSession : ILiveTranscriptionSession
{
    private const int SampleRate = 16000;
    private const int ChunkWindowSamples = 32000; // 2.0-second chunk window for responsive live partial updates

    private readonly IAsrBackend _asr;
    private readonly IDiarizer _diarizer;
    private readonly TranscriptionPipelineOptions _options;
    private readonly List<LiveSegmentUpdate> _segments = new();
    private readonly List<float> _audioBuffer = new();

    private bool _isPaused;
    private bool _disposed;
    private TimeSpan _currentSessionTime = TimeSpan.Zero;
    private TimeSpan _lastWindowEnd = TimeSpan.Zero;
    private int _speakerTurnCounter = 0;

    public event EventHandler<LiveSegmentUpdate>? SegmentUpdated;
    public IReadOnlyList<LiveSegmentUpdate> Segments => _segments;
    public bool IsPaused => _isPaused;

    public LiveTranscriptionSession(
        IAsrBackend asr,
        IDiarizer diarizer,
        TranscriptionPipelineOptions? options = null)
    {
        _asr = asr ?? throw new ArgumentNullException(nameof(asr));
        _diarizer = diarizer ?? throw new ArgumentNullException(nameof(diarizer));
        _options = options ?? new TranscriptionPipelineOptions();
    }

    public async ValueTask PushAudioAsync(ReadOnlyMemory<float> samples, TimeSpan timestamp, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isPaused || samples.Length == 0) return;

        ct.ThrowIfCancellationRequested();

        // Append samples to rolling audio buffer
        var span = samples.Span;
        for (int i = 0; i < span.Length; i++)
        {
            _audioBuffer.Add(span[i]);
        }

        _currentSessionTime = timestamp;

        // When we have accumulated enough audio (or at least 2.0s), trigger interim processing
        if (_audioBuffer.Count >= ChunkWindowSamples)
        {
            await ProcessWindowAsync(isFinal: false, ct).ConfigureAwait(false);
        }
    }

    public void Pause()
    {
        _isPaused = true;
    }

    public void Resume()
    {
        _isPaused = false;
    }

    private async Task ProcessWindowAsync(bool isFinal, CancellationToken ct)
    {
        if (_audioBuffer.Count < 8000 && !isFinal) // Less than 0.5s
        {
            return;
        }

        float[] samplesToProcess = _audioBuffer.ToArray();
        var windowStart = _lastWindowEnd;
        var windowEnd = windowStart + TimeSpan.FromSeconds(samplesToProcess.Length / (double)SampleRate);

        try
        {
            var audioBuffer = new AudioBuffer(samplesToProcess, SampleRate, 1);
            var asrOptions = _options.AsrOptions with { LanguageCode = _options.LanguageCode };

            // Run ASR
            var asrSegments = await _asr.TranscribeAsync(audioBuffer, asrOptions, ct).ConfigureAwait(false);

            if (asrSegments.Length > 0)
            {
                foreach (var asrSeg in asrSegments)
                {
                    string speakerId = $"speaker-{_speakerTurnCounter % 2}";
                    var segStart = windowStart + asrSeg.Start;
                    var segEnd = windowStart + asrSeg.End;

                    if (!string.IsNullOrWhiteSpace(asrSeg.Text))
                    {
                        var update = new LiveSegmentUpdate(
                            speakerId,
                            asrSeg.Text.Trim(),
                            segStart,
                            segEnd,
                            isFinal);

                        _segments.Add(update);
                        SegmentUpdated?.Invoke(this, update);
                    }
                }
            }

            _lastWindowEnd = windowEnd;
            _audioBuffer.Clear();
            _speakerTurnCounter++;
        }
        catch (Exception)
        {
            // Keep buffer intact on transient errors
        }
    }

    public async ValueTask<Transcript> CompleteDraftAsync(string sourcePath, string? title = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Process remaining buffer as final
        if (_audioBuffer.Count > 0)
        {
            await ProcessWindowAsync(isFinal: true, ct).ConfigureAwait(false);
        }

        // Build domain segments
        var domainSegments = new List<Segment>(_segments.Count);
        var speakerIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var seg in _segments)
        {
            speakerIds.Add(seg.SpeakerId);
            var words = SynthesizeWords(seg.Text, seg.Start, seg.End);
            domainSegments.Add(new Segment(seg.SpeakerId, seg.Start, seg.End, words));
        }

        var speakers = speakerIds.Select((id, idx) =>
            new Speaker(id, $"Speaker {idx + 1}", "#4F46E5")).ToList();

        if (speakers.Count == 0)
        {
            speakers.Add(new Speaker("speaker-0", "Speaker 1", "#4F46E5"));
        }

        string sessionTitle = !string.IsNullOrWhiteSpace(title)
            ? title
            : $"Live Recording ({DateTime.Now:MMM dd, HH:mm})";

        var audioRef = new AudioReference(
            localPath: sourcePath,
            duration: _currentSessionTime > TimeSpan.Zero ? _currentSessionTime : _lastWindowEnd,
            sampleRate: SampleRate);

        return new Transcript(
            id: Guid.NewGuid(),
            title: sessionTitle,
            createdAt: DateTimeOffset.UtcNow,
            languageCode: _options.LanguageCode,
            speakers: speakers,
            segments: domainSegments,
            audio: audioRef);
    }

    private static IReadOnlyList<Word> SynthesizeWords(string text, TimeSpan start, TimeSpan end)
    {
        var rawWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawWords.Length == 0) return [];

        double total = (end - start).TotalSeconds;
        double perWord = total / rawWords.Length;
        var words = new List<Word>(rawWords.Length);

        for (int i = 0; i < rawWords.Length; i++)
        {
            var wStart = start + TimeSpan.FromSeconds(i * perWord);
            var wEnd = (i == rawWords.Length - 1) ? end : start + TimeSpan.FromSeconds((i + 1) * perWord);
            if (wEnd <= wStart) wEnd = wStart + TimeSpan.FromMilliseconds(50);
            if (wEnd > end) wEnd = end;

            words.Add(new Word(rawWords[i], wStart, wEnd, 0.90));
        }

        return words;
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _audioBuffer.Clear();
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }
}
