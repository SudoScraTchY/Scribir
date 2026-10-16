namespace Scriber.Inference.SpeakerId;

using SherpaOnnx;
using Scriber.Core.Audio;
using Scriber.Core.Inference;

/// <summary>
/// Configuration for the sherpa-onnx speaker embedding extractor.
/// </summary>
public sealed class SherpaSpeakerEmbedderConfig
{
    public required string ModelPath { get; init; }
    public int NumThreads { get; init; } = 2;
    public int Debug { get; init; } = 0;
    public string Provider { get; init; } = "cpu";
}

/// <summary>
/// On-device speaker embedding extractor powered by sherpa-onnx (ADR-0003, ADR-0010).
/// Extracts fixed-dimensional embedding vectors (e.g. 192-d or 512-d) from 16kHz speech audio.
/// </summary>
public sealed class SherpaSpeakerEmbedder : ISpeakerEmbedder
{
    private readonly SpeakerEmbeddingExtractor _extractor;
    private bool _disposed;

    public int EmbeddingDimension => _extractor.Dim;

    public SherpaSpeakerEmbedder(SherpaSpeakerEmbedderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!File.Exists(config.ModelPath))
        {
            throw new FileNotFoundException($"Speaker embedding model not found at '{config.ModelPath}'.", config.ModelPath);
        }

        var extractorConfig = new SpeakerEmbeddingExtractorConfig
        {
            Model = config.ModelPath,
            NumThreads = config.NumThreads,
            Debug = config.Debug,
            Provider = config.Provider
        };

        _extractor = new SpeakerEmbeddingExtractor(extractorConfig);
    }

    public Task<float[]> ExtractEmbeddingAsync(AudioBuffer segment, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Samples.Length == 0)
        {
            throw new ArgumentException("Audio segment cannot be empty.", nameof(segment));
        }

        ct.ThrowIfCancellationRequested();

        using var stream = _extractor.CreateStream();
        stream.AcceptWaveform(segment.SampleRate, segment.Samples);
        stream.InputFinished();

        var embedding = _extractor.Compute(stream);
        return Task.FromResult(embedding ?? Array.Empty<float>());
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _extractor.Dispose();
            _disposed = true;
        }
    }
}
