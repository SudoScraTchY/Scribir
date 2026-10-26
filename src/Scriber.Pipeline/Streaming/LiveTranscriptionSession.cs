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
    private readonly object _bufferLock = new();
    private readonly SemaphoreSlim _processLock = new(1, 1);

    private float[] _rentedBuffer;
    private int _bufferedCount;

    private bool _isPaused;
    private bool _disposed;
    private TimeSpan _currentSessionTime = TimeSpan.Zero;
    private TimeSpan _lastWindowEnd = TimeSpan.Zero;
    private int _speakerTurnCounter = 0;

    public event EventHandler<LiveSegmentUpdate>? SegmentUpdated;
    public IReadOnlyList<LiveSegmentUpdate> Segments
    {
        get
        {
            lock (_segments)
            {
                return _segments.ToList();
            }
        }
    }
    public bool IsPaused => _isPaused;

    public LiveTranscriptionSession(
        IAsrBackend asr,
        IDiarizer diarizer,
        TranscriptionPipelineOptions? options = null)
    {
        _asr = asr ?? throw new ArgumentNullException(nameof(asr));
        _diarizer = diarizer ?? throw new ArgumentNullException(nameof(diarizer));
        _options = options ?? new TranscriptionPipelineOptions();
        _rentedBuffer = ArrayPool<float>.Shared.Rent(64000);
        _bufferedCount = 0;
    }

    public async ValueTask PushAudioAsync(ReadOnlyMemory<float> samples, TimeSpan timestamp, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isPaused || samples.Length == 0) return;

        ct.ThrowIfCancellationRequested();

        bool shouldTrigger;
        lock (_bufferLock)
        {
            // Ensure buffer capacity with zero-alloc pooled array expansion if needed
            int neededCapacity = _bufferedCount + samples.Length;
            if (neededCapacity > _rentedBuffer.Length)
            {
                int newCapacity = Math.Max(_rentedBuffer.Length * 2, neededCapacity);
                float[] newBuffer = ArrayPool<float>.Shared.Rent(newCapacity);
                _rentedBuffer.AsSpan(0, _bufferedCount).CopyTo(newBuffer);
                ArrayPool<float>.Shared.Return(_rentedBuffer);
                _rentedBuffer = newBuffer;
            }

            // Zero-alloc direct span copy into rented buffer
            samples.Span.CopyTo(_rentedBuffer.AsSpan(_bufferedCount));
            _bufferedCount += samples.Length;
            _currentSessionTime = timestamp;

            shouldTrigger = _bufferedCount >= ChunkWindowSamples;
        }

        // When we have accumulated enough audio (at least 2.0s), trigger processing
        if (shouldTrigger)
        {
            await ProcessPendingAudioAsync(ct).ConfigureAwait(false);
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

    private async Task ProcessPendingAudioAsync(CancellationToken ct)
    {
        // Try acquiring process lock without blocking PushAudioAsync callers
        if (!await _processLock.WaitAsync(0, ct).ConfigureAwait(false))
        {
            // Another processing loop is active; it will process newly buffered audio in its loop
            return;
        }

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                float[] samplesToProcess;
                int sampleCount;
                lock (_bufferLock)
                {
                    if (_disposed || _bufferedCount < ChunkWindowSamples)
                    {
                        break;
                    }

                    sampleCount = ChunkWindowSamples;
                    samplesToProcess = new float[sampleCount];
                    _rentedBuffer.AsSpan(0, sampleCount).CopyTo(samplesToProcess);

                    // Shift remaining unprocessed samples to beginning of buffer
                    int remaining = _bufferedCount - sampleCount;
                    if (remaining > 0)
                    {
                        _rentedBuffer.AsSpan(sampleCount, remaining).CopyTo(_rentedBuffer.AsSpan(0));
                    }
                    _bufferedCount = remaining;
                }

                await RunWindowInferenceAsync(samplesToProcess, sampleCount, isFinal: false, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _processLock.Release();
        }
    }

    private async Task RunWindowInferenceAsync(float[] samples, int sampleCount, bool isFinal, CancellationToken ct)
    {
        var windowStart = _lastWindowEnd;
        var windowEnd = windowStart + TimeSpan.FromSeconds(sampleCount / (double)SampleRate);

        try
        {
            var audioBuffer = new AudioBuffer(samples, SampleRate, 1);
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

                        lock (_segments)
                        {
                            _segments.Add(update);
                        }
                        SegmentUpdated?.Invoke(this, update);
                    }
                }
            }

            _lastWindowEnd = windowEnd;
            _speakerTurnCounter++;
        }
        catch (Exception)
        {
            // Keep window tracking continuous even on transient errors
            _lastWindowEnd = windowEnd;
        }
    }

    public async ValueTask<Transcript> CompleteDraftAsync(string sourcePath, string? title = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Wait for any in-flight window processing to finish
        await _processLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Process remaining buffer as final if sufficient audio exists
            float[]? remainingSamples = null;
            int remainingCount = 0;

            lock (_bufferLock)
            {
                if (_bufferedCount >= 8000) // At least 0.5s of audio
                {
                    remainingCount = _bufferedCount;
                    remainingSamples = new float[remainingCount];
                    _rentedBuffer.AsSpan(0, remainingCount).CopyTo(remainingSamples);
                    _bufferedCount = 0;
                }
                else
                {
                    _bufferedCount = 0;
                }
            }

            if (remainingSamples != null)
            {
                await RunWindowInferenceAsync(remainingSamples, remainingCount, isFinal: true, ct).ConfigureAwait(false);
            }

            List<LiveSegmentUpdate> segmentsCopy;
            lock (_segments)
            {
                segmentsCopy = _segments.ToList();
            }

            // Build domain segments
            var domainSegments = new List<Segment>(segmentsCopy.Count);
            var speakerIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var seg in segmentsCopy)
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
        finally
        {
            _processLock.Release();
        }
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
            _disposed = true;
            lock (_bufferLock)
            {
                var buf = Interlocked.Exchange(ref _rentedBuffer, null!);
                if (buf != null)
                {
                    ArrayPool<float>.Shared.Return(buf);
                }
                _bufferedCount = 0;
            }
            _processLock.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
