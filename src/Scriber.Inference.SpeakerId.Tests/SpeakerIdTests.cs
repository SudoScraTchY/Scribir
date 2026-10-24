namespace Scriber.Inference.SpeakerId.Tests;

using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Scriber.Core.Inference;
using Scriber.Inference.SpeakerId;
using Xunit;

public class SpeakerIdTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        float[] a = [1f, 2f, 3f];
        float[] b = [1f, 2f, 3f];
        float sim = SpeakerEmbeddingMath.CosineSimilarity(a, b);
        Assert.Equal(1f, sim, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [0f, 1f, 0f];
        float sim = SpeakerEmbeddingMath.CosineSimilarity(a, b);
        Assert.Equal(0f, sim, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsMinusOne()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [-1f, 0f, 0f];
        float sim = SpeakerEmbeddingMath.CosineSimilarity(a, b);
        Assert.Equal(-1f, sim, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_MismatchedOrEmpty_ReturnsZero()
    {
        float[] a = [1f, 2f];
        float[] b = [1f, 2f, 3f];
        Assert.Equal(0f, SpeakerEmbeddingMath.CosineSimilarity(a, b));
        Assert.Equal(0f, SpeakerEmbeddingMath.CosineSimilarity(Array.Empty<float>(), a));
    }

    [Fact]
    public void AverageAndNormalize_ComputesUnitLengthAveragedVector()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [0f, 1f, 0f];
        float[] dest = new float[3];

        SpeakerEmbeddingMath.AverageAndNormalize(a, b, dest);

        // a + b = [1, 1, 0], (a+b)/2 = [0.5, 0.5, 0], norm = sqrt(0.5) ~ 0.7071
        // normalized = [1/sqrt(2), 1/sqrt(2), 0]
        float expected = 1f / MathF.Sqrt(2f);
        Assert.Equal(expected, dest[0], precision: 5);
        Assert.Equal(expected, dest[1], precision: 5);
        Assert.Equal(0f, dest[2], precision: 5);

        // L2 norm of dest must be 1.0
        float norm = MathF.Sqrt(dest[0] * dest[0] + dest[1] * dest[1] + dest[2] * dest[2]);
        Assert.Equal(1f, norm, precision: 5);
    }

    [Fact]
    public async Task InMemorySpeakerEmbedder_ExtractsDeterministicNormalizedEmbedding()
    {
        using var embedder = new InMemorySpeakerEmbedder(dimension: 64);
        Assert.Equal(64, embedder.EmbeddingDimension);

        float[] samples = new float[16000 * 2]; // 2 seconds at 16kHz
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = MathF.Sin(2f * MathF.PI * 440f * i / 16000f);
        }

        var buffer = new AudioBuffer(samples, 16000, 1);

        var emb1 = await embedder.ExtractEmbeddingAsync(buffer);
        var emb2 = await embedder.ExtractEmbeddingAsync(buffer);

        Assert.Equal(64, emb1.Length);
        Assert.Equal(64, emb2.Length);

        // Verify determinism
        Assert.Equal(emb1, emb2);

        // Verify L2 norm is ~1.0
        float norm = 0f;
        foreach (var v in emb1) norm += v * v;
        Assert.Equal(1f, MathF.Sqrt(norm), precision: 4);
    }

    [Fact]
    public void SpeakerMatcher_MatchesAboveThreshold()
    {
        var matcher = new SpeakerMatcher(defaultThreshold: 0.70f);

        var vpAlice = new Voiceprint(Guid.NewGuid(), "Alice", new float[] { 1f, 0f, 0f }, DateTimeOffset.UtcNow);
        var vpBob = new Voiceprint(Guid.NewGuid(), "Bob", new float[] { 0f, 1f, 0f }, DateTimeOffset.UtcNow);

        // Query vector very close to Alice
        float[] queryEmbedding = [0.99f, 0.05f, 0.0f];

        var result = matcher.Match("Speaker 0", queryEmbedding, [vpAlice, vpBob]);

        Assert.Equal("Speaker 0", result.DiarizedSpeakerId);
        Assert.NotNull(result.BestMatch);
        Assert.Equal("Alice", result.BestMatch.Voiceprint.DisplayName);
        Assert.True(result.MeetsThreshold);
        Assert.True(result.BestMatch.Similarity > 0.90f);
    }

    [Fact]
    public void SpeakerMatcher_DoesNotMeetThresholdWhenBelow()
    {
        var matcher = new SpeakerMatcher(defaultThreshold: 0.85f);

        var vpAlice = new Voiceprint(Guid.NewGuid(), "Alice", new float[] { 1f, 0f, 0f }, DateTimeOffset.UtcNow);

        // Query vector has low similarity (~0.70)
        float[] queryEmbedding = [0.70f, 0.70f, 0.0f];

        var result = matcher.Match("Speaker 1", queryEmbedding, [vpAlice]);

        Assert.NotNull(result.BestMatch);
        Assert.False(result.MeetsThreshold);
        Assert.True(result.BestMatch.Similarity < 0.85f);
    }

    [Fact]
    public void SpeakerMatcher_EmptyVoiceprints_ReturnsNoMatch()
    {
        var matcher = new SpeakerMatcher(0.60f);
        float[] queryEmbedding = [1f, 0f, 0f];

        var result = matcher.Match("Speaker X", queryEmbedding, Array.Empty<Voiceprint>());

        Assert.Null(result.BestMatch);
        Assert.False(result.MeetsThreshold);
    }

    [Fact]
    public void SherpaSpeakerEmbedder_MissingFile_ThrowsFileNotFoundException()
    {
        var config = new SherpaSpeakerEmbedderConfig
        {
            ModelPath = "non_existent_model_weights.onnx"
        };

        Assert.Throws<FileNotFoundException>(() => new SherpaSpeakerEmbedder(config));
    }

    [Fact]
    public async Task SherpaSpeakerEmbedder_RealOnnxModel_ExtractsValidEmbedding()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string modelPath = Path.Combine(repoRoot, "spikes", "PH0-SPIKE-001", "artifacts", "sherpa-models", "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx");
        if (!File.Exists(modelPath)) return;

        var config = new SherpaSpeakerEmbedderConfig
        {
            ModelPath = modelPath,
            NumThreads = 2
        };

        using var embedder = new SherpaSpeakerEmbedder(config);
        Assert.True(embedder.EmbeddingDimension > 0, $"Expected dimension > 0, got {embedder.EmbeddingDimension}");

        float[] samples = new float[16000 * 3];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = MathF.Sin(2f * MathF.PI * 440f * i / 16000f) * 0.5f;
        }
        var buffer = new AudioBuffer(samples, 16000, 1);

        var embedding = await embedder.ExtractEmbeddingAsync(buffer);
        Assert.NotNull(embedding);
        Assert.Equal(embedder.EmbeddingDimension, embedding.Length);
    }
}
