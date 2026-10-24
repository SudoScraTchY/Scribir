using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Scriber.Core.Inference;
using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;
using Scriber.Inference.SpeakerId;
using Scriber.Pipeline.Alignment;
using Scriber.Pipeline.Import;
using Scriber.Pipeline.Normalization;
using Scriber.Pipeline.Vad;

namespace Scriber.Pipeline;

/// <summary>
/// Default orchestrator for the complete audio-to-transcript processing pipeline.
/// Chains IAudioDecoder, IVad (optional), IDiarizer, IAsrBackend, ITranscriptAligner, ITextNormalizer (optional), and voiceprint recognition (optional).
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
    private readonly ISpeakerEmbedder? _speakerEmbedder;
    private readonly SpeakerMatcher? _speakerMatcher;
    private readonly IReadOnlyList<Voiceprint>? _knownVoiceprints;

    public TranscriptionPipeline(
        IAudioDecoder decoder,
        IDiarizer diarizer,
        IAsrBackend asr,
        ITranscriptAligner? aligner = null,
        IVad? vad = null,
        ITextNormalizer? normalizer = null,
        ISpeakerEmbedder? speakerEmbedder = null,
        SpeakerMatcher? speakerMatcher = null,
        IReadOnlyList<Voiceprint>? knownVoiceprints = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _diarizer = diarizer ?? throw new ArgumentNullException(nameof(diarizer));
        _asr = asr ?? throw new ArgumentNullException(nameof(asr));
        _aligner = aligner ?? new TranscriptAligner();
        _vad = vad;
        _normalizer = normalizer;
        _speakerEmbedder = speakerEmbedder;
        _speakerMatcher = speakerMatcher;
        _knownVoiceprints = knownVoiceprints;
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
            var speechRanges = _vad.DetectSpeech(audio, ct);
            if (speechRanges.Count == 0)
            {
                progress?.Report(new PipelineProgress(PipelineStage.Completed, 1.0, "No speech detected in audio."));
                return new Transcript(
                    id: Guid.NewGuid(),
                    title: !string.IsNullOrWhiteSpace(options.Title) ? options.Title : Path.GetFileNameWithoutExtension(sourcePath),
                    createdAt: DateTimeOffset.UtcNow,
                    languageCode: options.LanguageCode,
                    speakers: [],
                    segments: [],
                    audio: new AudioReference(sourcePath, audio.Duration, audio.SampleRate));
            }
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

        // 7. Assemble Speakers and Voiceprint Matching
        var speakers = await BuildSpeakersAsync(alignedSegments, turns, audio, ct).ConfigureAwait(false);

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

    private async Task<IReadOnlyList<Speaker>> BuildSpeakersAsync(
        IReadOnlyList<Segment> segments,
        IReadOnlyList<SpeakerTurn> turns,
        AudioBuffer audio,
        CancellationToken ct)
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
            Guid? matchedVoiceprintId = null;

            // Research & implementation of voiceprint feature: Match diarized speaker turns against known voiceprints
            if (_speakerEmbedder != null && _speakerMatcher != null && _knownVoiceprints != null && _knownVoiceprints.Count > 0)
            {
                try
                {
                    // Aggregate speech audio slices across turns for this speaker (up to 6s of audio for high-fidelity embedding)
                    var speakerTurns = turns.Where(t => t.SpeakerId == id).OrderByDescending(t => t.End - t.Start).ToList();
                    var collectedSamples = new List<float>();
                    int maxSamplesToCollect = audio.SampleRate * 6; // up to 6s

                    foreach (var turn in speakerTurns)
                    {
                        int startSample = Math.Clamp((int)(turn.Start.TotalSeconds * audio.SampleRate), 0, audio.Samples.Length - 1);
                        int endSample = Math.Clamp((int)(turn.End.TotalSeconds * audio.SampleRate), startSample + 1, audio.Samples.Length);
                        int turnLen = endSample - startSample;
                        if (turnLen <= 0) continue;

                        int take = Math.Min(turnLen, maxSamplesToCollect - collectedSamples.Count);
                        var slice = new float[take];
                        Array.Copy(audio.Samples, startSample, slice, 0, take);
                        collectedSamples.AddRange(slice);

                        if (collectedSamples.Count >= maxSamplesToCollect) break;
                    }

                    if (collectedSamples.Count >= 16000) // At least 1.0s of speech across turns
                    {
                        var sliceBuffer = new AudioBuffer(collectedSamples.ToArray(), audio.SampleRate, 1);
                        float[] embedding = await _speakerEmbedder.ExtractEmbeddingAsync(sliceBuffer, ct).ConfigureAwait(false);
                        var matchResult = _speakerMatcher.Match(id, embedding, _knownVoiceprints);

                        if (matchResult.MeetsThreshold && matchResult.BestMatch != null)
                        {
                            displayName = matchResult.BestMatch.Voiceprint.DisplayName;
                            matchedVoiceprintId = matchResult.BestMatch.Voiceprint.Id;
                        }
                    }
                }
                catch
                {
                    // Fall back to default generic speaker label if embedding fails
                }
            }

            speakers.Add(new Speaker(id, displayName, color, matchedVoiceprintId));
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
