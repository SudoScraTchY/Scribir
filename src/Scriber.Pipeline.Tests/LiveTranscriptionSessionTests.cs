using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Scriber.Inference.Asr;
using Scriber.Inference.Diarization;
using Scriber.Pipeline.Streaming;
using Xunit;

namespace Scriber.Pipeline.Tests;

public class LiveTranscriptionSessionTests
{
    private sealed class StubAsr : IAsrBackend
    {
        public string Id => "stub-asr";
        public string DisplayName => "Stub ASR";
        public IReadOnlyList<string> SupportedLanguageCodes => ["en"];
        public bool SupportsStreaming => false;

        public Task<TranscriptSegment[]> TranscribeAsync(AudioBuffer audio, AsrOptions options, CancellationToken ct = default)
        {
            var words = new Word[]
            {
                new("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(0.5), 0.95),
                new("live", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.0), 0.95),
                new("streaming", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.5), 0.95),
                new("world", TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.0), 0.95)
            };
            var seg = new TranscriptSegment("Hello live streaming world", TimeSpan.Zero, TimeSpan.FromSeconds(2.0), words);
            return Task.FromResult<TranscriptSegment[]>([seg]);
        }

        public async IAsyncEnumerable<PartialTranscriptSegment> TranscribeStreamingAsync(
            IAsyncEnumerable<AudioChunk> chunks,
            AsrOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield break;
        }
    }

    private sealed class StubDiarizer : IDiarizer
    {
        public Task<IReadOnlyList<SpeakerTurn>> DiarizeAsync(AudioBuffer audio, DiarizationOptions options, CancellationToken ct = default)
        {
            IReadOnlyList<SpeakerTurn> turns = [new SpeakerTurn("speaker-0", TimeSpan.Zero, TimeSpan.FromSeconds(2.0))];
            return Task.FromResult(turns);
        }
    }

    [Fact]
    public async Task PushAudioAsync_EmitsSegmentUpdate_WhenWindowFilled()
    {
        var asr = new StubAsr();
        var diarizer = new StubDiarizer();
        await using var session = new LiveTranscriptionSession(asr, diarizer);

        LiveSegmentUpdate? receivedUpdate = null;
        session.SegmentUpdated += (_, update) => receivedUpdate = update;

        // Push 32000 samples (2.0s at 16kHz)
        var samples = new float[32000];
        await session.PushAudioAsync(samples, TimeSpan.FromSeconds(2.0));

        Assert.NotNull(receivedUpdate);
        Assert.Equal("Hello live streaming world", receivedUpdate.Text);
        Assert.Equal(TimeSpan.Zero, receivedUpdate.Start);
        Assert.Equal(TimeSpan.FromSeconds(2.0), receivedUpdate.End);
    }

    [Fact]
    public async Task PauseAndResume_PreservesStateAndIgnoresPausedAudio()
    {
        var asr = new StubAsr();
        var diarizer = new StubDiarizer();
        await using var session = new LiveTranscriptionSession(asr, diarizer);

        int updateCount = 0;
        session.SegmentUpdated += (_, _) => updateCount++;

        session.Pause();
        Assert.True(session.IsPaused);

        // Audio pushed while paused must be ignored
        var samples = new float[32000];
        await session.PushAudioAsync(samples, TimeSpan.FromSeconds(2.0));
        Assert.Equal(0, updateCount);

        session.Resume();
        Assert.False(session.IsPaused);

        await session.PushAudioAsync(samples, TimeSpan.FromSeconds(4.0));
        Assert.Equal(1, updateCount);
    }

    [Fact]
    public async Task CompleteDraftAsync_ConstructsValidTranscript()
    {
        var asr = new StubAsr();
        var diarizer = new StubDiarizer();
        await using var session = new LiveTranscriptionSession(asr, diarizer);

        var samples = new float[32000];
        await session.PushAudioAsync(samples, TimeSpan.FromSeconds(2.0));

        var draft = await session.CompleteDraftAsync("live_recording.wav", "Test Live Recording");

        Assert.NotNull(draft);
        Assert.Equal("Test Live Recording", draft.Title);
        Assert.Single(draft.Segments);
        Assert.Equal("Hello live streaming world", draft.Segments[0].Text);
        Assert.NotEmpty(draft.Speakers);
        Assert.Equal("live_recording.wav", draft.Audio.LocalPath);
    }
}
