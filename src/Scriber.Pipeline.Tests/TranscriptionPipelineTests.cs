using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;
using Scriber.Pipeline.Alignment;
using Scriber.Pipeline.Import;
using Scriber.Pipeline.Normalization;
using Scriber.Pipeline.Vad;
using Xunit;

namespace Scriber.Pipeline.Tests;

public class TranscriptionPipelineTests
{
    private sealed class MockAudioDecoder : IAudioDecoder
    {
        public Task<AudioBuffer> DecodeAsync(string filePath, CancellationToken ct = default)
        {
            var samples = new float[16000 * 2]; // 2 seconds
            return Task.FromResult(new AudioBuffer(samples, 16000, 1));
        }
    }

    private sealed class MockDiarizer : IDiarizer
    {
        public Task<IReadOnlyList<SpeakerTurn>> DiarizeAsync(AudioBuffer audio, DiarizationOptions options, CancellationToken ct = default)
        {
            IReadOnlyList<SpeakerTurn> turns =
            [
                new SpeakerTurn("speaker-0", TimeSpan.Zero, TimeSpan.FromSeconds(1.0)),
                new SpeakerTurn("speaker-1", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(2.0))
            ];
            return Task.FromResult(turns);
        }
    }

    private sealed class MockAsrBackend : IAsrBackend
    {
        public string Id => "mock-asr";
        public string DisplayName => "Mock ASR Backend";
        public IReadOnlyList<string> SupportedLanguageCodes => ["en", "fa"];
        public bool SupportsStreaming => false;

        public Task<TranscriptSegment[]> TranscribeAsync(AudioBuffer audio, AsrOptions options, CancellationToken ct = default)
        {
            var seg1Words = new List<Word>
            {
                new("Hello", TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.4), 0.95),
                new("world", TimeSpan.FromSeconds(0.45), TimeSpan.FromSeconds(0.8), 0.92)
            };
            var seg2Words = new List<Word>
            {
                new("Second", TimeSpan.FromSeconds(1.1), TimeSpan.FromSeconds(1.4), 0.88),
                new("speaker", TimeSpan.FromSeconds(1.45), TimeSpan.FromSeconds(1.9), 0.91)
            };

            return Task.FromResult<TranscriptSegment[]>([
                new("Hello world", TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.8), seg1Words),
                new("Second speaker", TimeSpan.FromSeconds(1.1), TimeSpan.FromSeconds(1.9), seg2Words)
            ]);
        }

        public IAsyncEnumerable<PartialTranscriptSegment> TranscribeStreamingAsync(
            IAsyncEnumerable<AudioChunk> chunks, AsrOptions options, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }
    }

    [Fact]
    public async Task ProcessAsync_FullRun_ProducesValidTranscriptWithProgress()
    {
        var decoder = new MockAudioDecoder();
        var diarizer = new MockDiarizer();
        var asr = new MockAsrBackend();
        var normalizer = new PersianTextNormalizer();

        var pipeline = new TranscriptionPipeline(
            decoder: decoder,
            diarizer: diarizer,
            asr: asr,
            aligner: new TranscriptAligner(),
            normalizer: normalizer);

        var progressUpdates = new List<PipelineProgress>();
        var progress = new Progress<PipelineProgress>(p => progressUpdates.Add(p));

        var options = new TranscriptionPipelineOptions
        {
            Title = "Test Meeting",
            LanguageCode = "en"
        };

        var transcript = await pipeline.ProcessAsync("mock_audio.wav", options, progress);

        Assert.NotNull(transcript);
        Assert.Equal("Test Meeting", transcript.Title);
        Assert.Equal("en", transcript.LanguageCode);
        Assert.Equal("mock_audio.wav", transcript.Audio.LocalPath);
        Assert.Equal(TimeSpan.FromSeconds(2.0), transcript.Audio.Duration);

        // Speakers verification
        Assert.Equal(2, transcript.Speakers.Count);
        Assert.Equal("speaker-0", transcript.Speakers[0].Id);
        Assert.Equal("Speaker 1", transcript.Speakers[0].DisplayName);
        Assert.StartsWith("#", transcript.Speakers[0].ColorHex);
        Assert.Equal("speaker-1", transcript.Speakers[1].Id);
        Assert.Equal("Speaker 2", transcript.Speakers[1].DisplayName);

        // Segments verification
        Assert.Equal(2, transcript.Segments.Count);
        Assert.Equal("speaker-0", transcript.Segments[0].SpeakerId);
        Assert.Equal(2, transcript.Segments[0].Words.Count);
        Assert.Equal("speaker-1", transcript.Segments[1].SpeakerId);
        Assert.Equal(2, transcript.Segments[1].Words.Count);

        // Progress was reported
        Assert.NotEmpty(progressUpdates);
        Assert.Contains(progressUpdates, p => p.Stage == PipelineStage.Decoding);
        Assert.Contains(progressUpdates, p => p.Stage == PipelineStage.Diarizing);
        Assert.Contains(progressUpdates, p => p.Stage == PipelineStage.Transcribing);
        Assert.Contains(progressUpdates, p => p.Stage == PipelineStage.Completed);
    }

    [Fact]
    public async Task ProcessAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var pipeline = new TranscriptionPipeline(
            decoder: new MockAudioDecoder(),
            diarizer: new MockDiarizer(),
            asr: new MockAsrBackend());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pipeline.ProcessAsync("dummy.wav", ct: cts.Token));
    }

    private sealed class MockSpeakerEmbedder : Scriber.Core.Inference.ISpeakerEmbedder
    {
        public int EmbeddingDimension => 4;
        public Task<float[]> ExtractEmbeddingAsync(AudioBuffer segment, CancellationToken ct = default)
        {
            return Task.FromResult(new float[] { 1f, 0f, 0f, 0f });
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task ProcessAsync_WithSpeakerEmbedderAndVoiceprints_MatchesEnrolledSpeakers()
    {
        var voiceprintId = Guid.NewGuid();
        var voiceprint = new Voiceprint(
            id: voiceprintId,
            displayName: "Dr. Alice Smith",
            embedding: new float[] { 1f, 0f, 0f, 0f },
            createdAt: DateTimeOffset.UtcNow);

        var pipeline = new TranscriptionPipeline(
            decoder: new MockAudioDecoder(),
            diarizer: new MockDiarizer(),
            asr: new MockAsrBackend(),
            aligner: new TranscriptAligner(),
            normalizer: new PersianTextNormalizer(),
            speakerEmbedder: new MockSpeakerEmbedder(),
            speakerMatcher: new Scriber.Inference.SpeakerId.SpeakerMatcher(0.60f),
            knownVoiceprints: [voiceprint]);

        var options = new TranscriptionPipelineOptions { Title = "Voiceprint Match Test" };
        var transcript = await pipeline.ProcessAsync("test.wav", options);

        Assert.NotNull(transcript);
        Assert.Equal(2, transcript.Speakers.Count);

        var alice = transcript.Speakers.FirstOrDefault(s => s.DisplayName == "Dr. Alice Smith");
        Assert.NotNull(alice);
        Assert.Equal(voiceprintId, alice.VoiceprintId);
    }
}
