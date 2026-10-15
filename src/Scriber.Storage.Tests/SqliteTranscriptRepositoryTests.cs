using Microsoft.Data.Sqlite;
using Scriber.Core.Domain;
using Scriber.Storage.Migrations;
using Xunit;

namespace Scriber.Storage.Tests;

public class SqliteTranscriptRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTranscriptRepository _repo;

    public SqliteTranscriptRepositoryTests()
    {
        // In-memory SQLite connection for isolated, fast integration testing
        _connection = new SqliteConnection("Data Source=:memory:;Mode=Memory;Cache=Shared");
        _connection.Open();

        _repo = new SqliteTranscriptRepository(_connection);
    }

    public void Dispose()
    {
        _repo.Dispose();
        _connection.Dispose();
    }

    private static Transcript CreateSampleTranscript(Guid id, string title, string speaker1Text, string speaker2Text)
    {
        var speakers = new List<Speaker>
        {
            new("speaker-0", "Alice", "#2563EB"),
            new("speaker-1", "Bob", "#16A34A")
        };

        var words1 = speaker1Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((w, i) => new Word(w, TimeSpan.FromSeconds(i * 0.5), TimeSpan.FromSeconds((i + 1) * 0.5), 0.95))
            .ToList();

        var words2 = speaker2Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((w, i) => new Word(w, TimeSpan.FromSeconds(5.0 + i * 0.5), TimeSpan.FromSeconds(5.0 + (i + 1) * 0.5), 0.90))
            .ToList();

        var segments = new List<Segment>
        {
            new("speaker-0", words1[0].Start, words1[^1].End, words1),
            new("speaker-1", words2[0].Start, words2[^1].End, words2)
        };

        var audio = new AudioReference(
            localPath: $"audio_{id}.wav",
            duration: TimeSpan.FromSeconds(10.0),
            sampleRate: 16000);

        return new Transcript(
            id: id,
            title: title,
            createdAt: DateTimeOffset.UtcNow,
            languageCode: "en",
            speakers: speakers,
            segments: segments,
            audio: audio);
    }

    [Fact]
    public void Migrator_IsIdempotent()
    {
        // Migrations already applied by constructor, running again must succeed without error
        DatabaseMigrator.Migrate(_connection);
        DatabaseMigrator.Migrate(_connection);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        long count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveAndGetById_RoundTripsAllPropertiesAndInvariants()
    {
        var id = Guid.NewGuid();
        var original = CreateSampleTranscript(id, "Meeting on Neural Networks", "Exploring deep learning architectures", "Gradient descent convergence");

        await _repo.SaveAsync(original);

        var retrieved = await _repo.GetByIdAsync(id);
        Assert.NotNull(retrieved);

        Assert.Equal(original.Id, retrieved.Id);
        Assert.Equal(original.Title, retrieved.Title);
        Assert.Equal(original.LanguageCode, retrieved.LanguageCode);
        Assert.Equal(original.Audio.LocalPath, retrieved.Audio.LocalPath);
        Assert.Equal(original.Audio.Duration, retrieved.Audio.Duration);
        Assert.Equal(original.Audio.SampleRate, retrieved.Audio.SampleRate);

        Assert.Equal(2, retrieved.Speakers.Count);
        Assert.Equal("speaker-0", retrieved.Speakers[0].Id);
        Assert.Equal("Alice", retrieved.Speakers[0].DisplayName);
        Assert.Equal("#2563EB", retrieved.Speakers[0].ColorHex);
        Assert.Equal("speaker-1", retrieved.Speakers[1].Id);
        Assert.Equal("Bob", retrieved.Speakers[1].DisplayName);

        Assert.Equal(2, retrieved.Segments.Count);
        Assert.Equal("speaker-0", retrieved.Segments[0].SpeakerId);
        Assert.Equal(4, retrieved.Segments[0].Words.Count);
        Assert.Equal("Exploring", retrieved.Segments[0].Words[0].Text);

        Assert.Equal("speaker-1", retrieved.Segments[1].SpeakerId);
        Assert.Equal(3, retrieved.Segments[1].Words.Count);
        Assert.Equal("Gradient", retrieved.Segments[1].Words[0].Text);
    }

    [Fact]
    public async Task ListAsync_ReturnsSummariesWithAccurateCounts()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await _repo.SaveAsync(CreateSampleTranscript(id1, "Transcript 1", "first words", "second words"));
        await _repo.SaveAsync(CreateSampleTranscript(id2, "Transcript 2", "third words", "fourth words"));

        var list = await _repo.ListAsync();

        Assert.Equal(2, list.Count);
        var t1 = list.FirstOrDefault(s => s.Id == id1);
        Assert.NotNull(t1);
        Assert.Equal("Transcript 1", t1.Title);
        Assert.Equal(2, t1.SpeakerCount);
        Assert.Equal(2, t1.SegmentCount);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTranscriptAndCascades()
    {
        var id = Guid.NewGuid();
        var transcript = CreateSampleTranscript(id, "To Delete", "delete me", "also delete me");

        await _repo.SaveAsync(transcript);
        Assert.NotNull(await _repo.GetByIdAsync(id));

        await _repo.DeleteAsync(id);

        Assert.Null(await _repo.GetByIdAsync(id));
        var list = await _repo.ListAsync();
        Assert.DoesNotContain(list, s => s.Id == id);

        // FTS search must also return nothing
        var search = await _repo.SearchAsync("delete");
        Assert.Empty(search);
    }

    [Fact]
    public async Task SearchAsync_Fts5Query_FindsMatchingSegmentsAndSnippets()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await _repo.SaveAsync(CreateSampleTranscript(id1, "Biology Lecture", "Photosynthesis in green plants", "Chlorophyll absorption spectrum"));
        await _repo.SaveAsync(CreateSampleTranscript(id2, "Physics Seminar", "Quantum entanglement in photons", "Superposition principle"));

        // Exact term search
        var results = await _repo.SearchAsync("photosynthesis");
        Assert.Single(results);
        Assert.Equal(id1, results[0].TranscriptId);
        Assert.Equal("Biology Lecture", results[0].TranscriptTitle);
        Assert.Equal("speaker-0", results[0].SpeakerId);
        Assert.Contains("<b>Photosynthesis</b>", results[0].Snippet);

        // Prefix search
        var quantumResults = await _repo.SearchAsync("quant");
        Assert.Single(quantumResults);
        Assert.Equal(id2, quantumResults[0].TranscriptId);
        Assert.Contains("<b>Quantum</b>", quantumResults[0].Snippet);

        // Term present in segment 2
        var chlorophyllResults = await _repo.SearchAsync("chlorophyll");
        Assert.Single(chlorophyllResults);
        Assert.Equal("speaker-1", chlorophyllResults[0].SpeakerId);
    }

    [Fact]
    public async Task SearchAsync_PersianUnicode_MatchesCorrectly()
    {
        var id = Guid.NewGuid();
        var speakers = new List<Speaker> { new("speaker-0", "Ali", "#2563EB") };
        var words = new List<Word>
        {
            new("هوش", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), 0.9),
            new("مصنوعی", TimeSpan.FromSeconds(1.1), TimeSpan.FromSeconds(2), 0.95),
            new("پیشرفته", TimeSpan.FromSeconds(2.1), TimeSpan.FromSeconds(3), 0.92)
        };
        var segments = new List<Segment> { new("speaker-0", TimeSpan.Zero, TimeSpan.FromSeconds(3), words) };
        var audio = new AudioReference("persian.wav", TimeSpan.FromSeconds(5), 16000);
        var transcript = new Transcript(id, "جلسه هوش مصنوعی", DateTimeOffset.UtcNow, "fa", speakers, segments, audio);

        await _repo.SaveAsync(transcript);

        var results = await _repo.SearchAsync("مصنوعی");
        Assert.Single(results);
        Assert.Equal("جلسه هوش مصنوعی", results[0].TranscriptTitle);
        Assert.Contains("مصنوعی", results[0].Snippet);
    }
}
