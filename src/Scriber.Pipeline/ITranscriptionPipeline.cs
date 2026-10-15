using Scriber.Core.Audio;
using Scriber.Core.Domain;

namespace Scriber.Pipeline;

/// <summary>
/// End-to-end transcription orchestrator contract: executes decoding, VAD, diarization, ASR, alignment, and normalization.
/// </summary>
public interface ITranscriptionPipeline
{
    Task<Transcript> ProcessAsync(
        string audioFilePath,
        TranscriptionPipelineOptions? options = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken ct = default);

    Task<Transcript> ProcessAsync(
        AudioBuffer audio,
        string sourcePath,
        TranscriptionPipelineOptions? options = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken ct = default);
}
