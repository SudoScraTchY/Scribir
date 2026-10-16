namespace Scriber.Storage.Tests;

using Microsoft.Data.Sqlite;
using Scriber.Core.Domain;
using Scriber.Storage;
using Xunit;

public class TranscriptVersioningTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTranscriptRepository _repository;

    public TranscriptVersioningTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _repository = new SqliteTranscriptRepository(_connection);
    }

    public void Dispose()
    {
        _repository.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ArchiveVersionAsync_PersistsHistoricalSnapshotsNonDestructively()
    {
        var audio = new AudioReference("test.wav", TimeSpan.FromMinutes(2), 16000);
        var speaker = new Speaker("spk_0", "Dr. Jane Smith", "#2563EB");
        var wordsV1 = new List<Word>
        {
            new("First", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.5), 0.95f),
            new("version", TimeSpan.FromSeconds(0.6), TimeSpan.FromSeconds(1.0), 0.95f)
        };
        var segV1 = new Segment("spk_0", TimeSpan.Zero, TimeSpan.FromSeconds(1.1), wordsV1);
        var transcriptId = Guid.NewGuid();
        var transcriptV1 = new Transcript(transcriptId, "Sprint Review", DateTimeOffset.UtcNow, "en", [speaker], [segV1], audio);

        await _repository.SaveAsync(transcriptV1);

        // 1. Archive Version 1 before edits or re-transcription
        Guid v1Id = await _repository.ArchiveVersionAsync(transcriptId, "Initial human transcription");

        // 2. Modify and re-save transcript as Version 2
        var wordsV2 = new List<Word>
        {
            new("Second", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.5), 0.99f),
            new("iteration", TimeSpan.FromSeconds(0.6), TimeSpan.FromSeconds(1.0), 0.99f),
            new("enhanced", TimeSpan.FromSeconds(1.1), TimeSpan.FromSeconds(1.5), 0.99f)
        };
        var segV2 = new Segment("spk_0", TimeSpan.Zero, TimeSpan.FromSeconds(1.6), wordsV2);
        var transcriptV2 = transcriptV1 with { Segments = [segV2] };
        await _repository.SaveAsync(transcriptV2);

        // 3. Archive Version 2
        Guid v2Id = await _repository.ArchiveVersionAsync(transcriptId, "Model re-transcription upgrade");

        // 4. Verify version list
        var versions = await _repository.GetVersionsAsync(transcriptId);
        Assert.Equal(2, versions.Count);
        Assert.Equal(2, versions[0].VersionNumber);
        Assert.Equal("Model re-transcription upgrade", versions[0].Note);
        Assert.Equal(1, versions[1].VersionNumber);
        Assert.Equal("Initial human transcription", versions[1].Note);

        // 5. Restore V1 and verify it preserved original state
        var restoredV1 = await _repository.GetVersionAsync(v1Id);
        Assert.NotNull(restoredV1);
        Assert.Equal("First version", string.Join(" ", restoredV1!.Segments[0].Words.Select(w => w.Text)));

        // 6. Restore V2 and verify it matches enhanced state
        var restoredV2 = await _repository.GetVersionAsync(v2Id);
        Assert.NotNull(restoredV2);
        Assert.Equal("Second iteration enhanced", string.Join(" ", restoredV2!.Segments[0].Words.Select(w => w.Text)));
    }
}
