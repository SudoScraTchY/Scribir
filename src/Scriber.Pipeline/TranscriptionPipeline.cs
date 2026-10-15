using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;
using Scriber.Pipeline.Alignment;
using Scriber.Pipeline.Import;
using Scriber.Pipeline.Normalization;
using Scriber.Pipeline.Vad;

namespace Scriber.Pipeline;

/// <summary>
/// Default orchestrator for the complete audio-to-transcript processing pipeline.
/// Chains IAudioDecoder, IVad (optional), IDiarizer, IAsrBackend, ITranscriptAligner, and ITextNormalizer (optional).
/// </summary>
public sealed class TranscriptionPipeline : ITranscriptionPipeline
{
    private static readonly string[] DefaultColorPalette =
    [
        "#2563EB", // Blue
        "#16A34A", // Green
        "#D97706", // Amber
        "#9333EA", // Purple
        "#DC2626", // Red
        "#0D9488", // Teal
        "#EA580C", // Orange
        "#4F46E5"  // Indigo
    ];

    private readonly IAudioDecoder _decoder;
    private readonly IVad? _vad;
    private readonly IDiarizer _diarizer;
    private readonly IAsrBackend _asr;
    private readonly ITranscriptAligner _aligner;
    private readonly ITextNormalizer? _normalizer;

    public TranscriptionPipeline(
        IAudioDecoder decoder,
        IDiarizer diarizer,
        IAsrBackend asr,
        ITranscriptAligner? aligner = null,
        IVad? vad = null,
        ITextNormalizer? normalizer = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _diarizer = diarizer ?? throw new ArgumentNullException(nameof(diarizer));
        _asr = asr ?? throw new ArgumentNullException(nameof(asr));
        _aligner = aligner ?? new TranscriptAligner();
        _vad = vad;
        _normalizer = normalizer;
    }

    public async Task<Transcript> ProcessAsync(
        string audioFilePath,
        TranscriptionPipelineOptions? options = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath, nameof(audioFilePath));
        ct.ThrowIfCancellationRequested();

        options ??= new TranscriptionPipelineOptions();

        // 1. Decode Audio
        progress?.Report(new PipelineProgress(PipelineStage.Decoding, 0.05, $"Decoding audio from {Path.GetFileName(audioFilePath)}..."));
        var audioBuffer = await _decoder.DecodeAsync(audioFilePath, ct).ConfigureAwait(false);

        return await ProcessCoreAsync(audioBuffer, audioFilePath, options, progress, ct).ConfigureAwait(false);
    }

    public async Task<Transcript> ProcessAsync(
        AudioBuffer audio,
        string sourcePath,
        TranscriptionPipelineOptions? options = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audio, nameof(audio));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath, nameof(sourcePath));
        ct.ThrowIfCancellationRequested();

        options ??= new TranscriptionPipelineOptions();
        return await ProcessCoreAsync(audio, sourcePath, options, progress, ct).ConfigureAwait(false);
    }

    private async Task<Transcript> ProcessCoreAsync(
        AudioBuffer audio,
        string sourcePath,
        TranscriptionPipelineOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken ct)
    {
        // 2. VAD Pass (optional)
        if (options.EnableVad && _vad != null)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new PipelineProgress(PipelineStage.VoiceActivityDetection, 0.15, "Performing Voice Activity Detection..."));
            _ = _vad.DetectSpeech(audio, ct);
        }

        // 3. Diarization Pass
        ct.ThrowIfCancellationRequested();
        progress?.Report(new PipelineProgress(PipelineStage.Diarizing, 0.30, "Identifying speaker turns..."));
        var turns = await _diarizer.DiarizeAsync(audio, options.DiarizationOptions, ct).ConfigureAwait(false);

        // 4. ASR Pass
        ct.ThrowIfCancellationRequested();
        progress?.Report(new PipelineProgress(PipelineStage.Transcribing, 0.60, $"Transcribing speech ({options.LanguageCode})..."));
        var asrOptions = options.AsrOptions with { LanguageCode = options.LanguageCode };
        var asrSegments = await _asr.TranscribeAsync(audio, asrOptions, ct).ConfigureAwait(false);

        // 5. Alignment Pass
        ct.ThrowIfCancellationRequested();
        progress?.Report(new PipelineProgress(PipelineStage.Aligning, 0.85, "Aligning speech tokens to speakers..."));
        var alignedSegments = _aligner.Align(turns, asrSegments);

        // 6. Normalization Pass (optional)
        if (_normalizer != null)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new PipelineProgress(PipelineStage.Normalizing, 0.95, "Normalizing text and punctuation..."));
            alignedSegments = _normalizer.NormalizeSegments(alignedSegments, options.NormalizationOptions);
        }

        // 7. Assemble Speakers and Transcript
        var speakers = BuildSpeakers(alignedSegments, turns);

        var title = !string.IsNullOrWhiteSpace(options.Title)
            ? options.Title
            : Path.GetFileNameWithoutExtension(sourcePath);

        if (string.IsNullOrWhiteSpace(title))
            title = "Untitled Transcript";

        var audioRef = new AudioReference(
            localPath: sourcePath,
            duration: audio.Duration,
            sampleRate: audio.SampleRate);

        var transcript = new Transcript(
            id: Guid.NewGuid(),
            title: title,
            createdAt: DateTimeOffset.UtcNow,
            languageCode: options.LanguageCode,
            speakers: speakers,
            segments: alignedSegments,
            audio: audioRef);

        progress?.Report(new PipelineProgress(PipelineStage.Completed, 1.0, "Transcription pipeline completed successfully."));
        return transcript;
    }

    private static IReadOnlyList<Speaker> BuildSpeakers(IReadOnlyList<Segment> segments, IReadOnlyList<SpeakerTurn> turns)
    {
        // Collect all distinct speaker IDs preserving appearance order
        var orderedIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var seg in segments)
        {
            if (seen.Add(seg.SpeakerId))
                orderedIds.Add(seg.SpeakerId);
        }

        foreach (var turn in turns)
        {
            if (seen.Add(turn.SpeakerId))
                orderedIds.Add(turn.SpeakerId);
        }

        if (orderedIds.Count == 0 && segments.Count > 0)
        {
            orderedIds.Add("speaker-0");
        }

        var speakers = new List<Speaker>(orderedIds.Count);
        for (int i = 0; i < orderedIds.Count; i++)
        {
            string id = orderedIds[i];
            string displayName = FormatDisplayName(id, i);
            string color = DefaultColorPalette[i % DefaultColorPalette.Length];
            speakers.Add(new Speaker(id, displayName, color));
        }

        return speakers;
    }

    private static string FormatDisplayName(string speakerId, int index)
    {
        if (speakerId.StartsWith("speaker-", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(speakerId.AsSpan("speaker-".Length), out int num))
        {
            return $"Speaker {num + 1}";
        }

        return $"Speaker {index + 1}";
    }
}
