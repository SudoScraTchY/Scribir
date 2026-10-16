namespace Scriber.Storage.Tests;

using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Scriber.Storage;
using Xunit;

public class VoiceprintRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteVoiceprintRepository _repo;

    public VoiceprintRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"scriber_vp_test_{Guid.NewGuid():N}.db");
        _repo = new SqliteVoiceprintRepository(_dbPath);
    }

    public void Dispose()
    {
        _repo.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task SaveAndGetById_RoundTripsEmbeddingExactMatch()
    {
        var id = Guid.NewGuid();
        float[] originalEmbedding = [0.123f, -0.456f, 0.789f, 0.001f, 1.5f];
        var now = DateTimeOffset.UtcNow;
        var vp = new Voiceprint(id, "Dr. Alice Smith", originalEmbedding, now);

        await _repo.SaveAsync(vp);

        var retrieved = await _repo.GetByIdAsync(id);

        Assert.NotNull(retrieved);
        Assert.Equal(id, retrieved.Id);
        Assert.Equal("Dr. Alice Smith", retrieved.DisplayName);
        Assert.Equal(originalEmbedding.Length, retrieved.Embedding.Length);

        var retrievedFloats = retrieved.Embedding.ToArray();
        for (int i = 0; i < originalEmbedding.Length; i++)
        {
            Assert.Equal(originalEmbedding[i], retrievedFloats[i], precision: 5);
        }
    }

    [Fact]
    public async Task GetAllAsync_ReturnsNewestFirst()
    {
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t3 = DateTimeOffset.UtcNow;

        var vp1 = new Voiceprint(Guid.NewGuid(), "User 1", new float[] { 1f, 0f }, t1);
        var vp2 = new Voiceprint(Guid.NewGuid(), "User 2", new float[] { 0f, 1f }, t2);
        var vp3 = new Voiceprint(Guid.NewGuid(), "User 3", new float[] { 1f, 1f }, t3);

        await _repo.SaveAsync(vp1);
        await _repo.SaveAsync(vp2);
        await _repo.SaveAsync(vp3);

        var all = await _repo.GetAllAsync();

        Assert.Equal(3, all.Count);
        Assert.Equal("User 3", all[0].DisplayName);
        Assert.Equal("User 2", all[1].DisplayName);
        Assert.Equal("User 1", all[2].DisplayName);
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingVoiceprint()
    {
        var id = Guid.NewGuid();
        var vp = new Voiceprint(id, "Initial Name", new float[] { 1f }, DateTimeOffset.UtcNow);
        await _repo.SaveAsync(vp);

        var updated = new Voiceprint(id, "Updated Name", new float[] { 2f, 3f }, DateTimeOffset.UtcNow);
        await _repo.SaveAsync(updated);

        var retrieved = await _repo.GetByIdAsync(id);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated Name", retrieved.DisplayName);
        Assert.Equal(2, retrieved.Embedding.Length);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var id = Guid.NewGuid();
        var vp = new Voiceprint(id, "To Delete", new float[] { 1f }, DateTimeOffset.UtcNow);
        await _repo.SaveAsync(vp);

        await _repo.DeleteAsync(id);

        var retrieved = await _repo.GetByIdAsync(id);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task TranscriptRepository_PreservesSpeakerVoiceprintId()
    {
        using var transcriptRepo = new SqliteTranscriptRepository(_dbPath);

        var vpId = Guid.NewGuid();
        var speaker = new Speaker("spk_0", "Alice", "#3B82F6", vpId);
        var word = new Word("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.98);
        var segment = new Segment("spk_0", TimeSpan.Zero, TimeSpan.FromSeconds(1), [word]);
        var audio = new AudioReference("temp.wav", TimeSpan.FromSeconds(1), 16000);

        var transcript = new Transcript(
            id: Guid.NewGuid(),
            title: "Voiceprint Linkage Test",
            createdAt: DateTimeOffset.UtcNow,
            languageCode: "en",
            speakers: [speaker],
            segments: [segment],
            audio: audio);

        await transcriptRepo.SaveAsync(transcript);

        var loaded = await transcriptRepo.GetByIdAsync(transcript.Id);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Speakers);
        Assert.Equal(vpId, loaded.Speakers[0].VoiceprintId);
    }
}
