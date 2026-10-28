namespace Scriber.Storage.Tests;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Scriber.Storage.Sync;
using Xunit;

public class SyncTests : IDisposable
{
    private readonly string _tempSyncDir;

    public SyncTests()
    {
        _tempSyncDir = Path.Combine(Path.GetTempPath(), "scriber_sync_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempSyncDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempSyncDir))
            {
                Directory.Delete(_tempSyncDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void Crypto_EncryptAndDecrypt_RoundTripsSuccessfully()
    {
        byte[] original = Encoding.UTF8.GetBytes("Persian transcription: متن آزمایش همگام‌سازی محلی");
        string passphrase = "super-secure-local-passphrase-123!";

        byte[] encrypted = SyncCrypto.Encrypt(original, passphrase);
        Assert.NotNull(encrypted);
        Assert.NotEqual(original, encrypted);

        byte[] decrypted = SyncCrypto.Decrypt(encrypted, passphrase);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Crypto_DecryptWithWrongPassphrase_ThrowsCryptographicException()
    {
        byte[] original = Encoding.UTF8.GetBytes("Secret payload");
        byte[] encrypted = SyncCrypto.Encrypt(original, "correct-password");

        Assert.Throws<CryptographicException>(() =>
        {
            SyncCrypto.Decrypt(encrypted, "wrong-password");
        });
    }

    [Fact]
    public void Crypto_TamperedPayload_ThrowsCryptographicException()
    {
        byte[] original = Encoding.UTF8.GetBytes("Integrity protected text");
        byte[] encrypted = SyncCrypto.Encrypt(original, "password123");

        // Tamper with the last byte
        encrypted[^1] ^= 0xFF;

        Assert.Throws<CryptographicException>(() =>
        {
            SyncCrypto.Decrypt(encrypted, "password123");
        });
    }

    [Fact]
    public async Task LocalFolderSyncProvider_PerformsBasicFileAndManifestOperations()
    {
        using var provider = new LocalFolderSyncProvider(_tempSyncDir);

        bool connected = await provider.TestConnectionAsync();
        Assert.True(connected);

        var manifest = new SyncManifest
        {
            DeviceId = "TestDevice-Alpha",
            LastModifiedUtc = DateTimeOffset.UtcNow
        };
        manifest.Items["item-1"] = new SyncItemRecord("item-1", "transcript", "transcripts/item-1.bundle", DateTimeOffset.UtcNow, "abc123hash");

        await provider.SaveRemoteManifestAsync(manifest);

        var loadedManifest = await provider.GetRemoteManifestAsync();
        Assert.NotNull(loadedManifest);
        Assert.Equal("TestDevice-Alpha", loadedManifest.DeviceId);
        Assert.True(loadedManifest.Items.ContainsKey("item-1"));

        byte[] testData = Encoding.UTF8.GetBytes("Sample bundle content");
        await provider.UploadItemAsync("transcripts/item-1.bundle", testData);

        byte[]? downloadedData = await provider.DownloadItemAsync("transcripts/item-1.bundle");
        Assert.NotNull(downloadedData);
        Assert.Equal(testData, downloadedData);
    }

    [Fact]
    public async Task LocalFolderSyncProvider_LeadingSlashes_StayWithinBaseDirectory()
    {
        using var provider = new LocalFolderSyncProvider(_tempSyncDir);

        byte[] testData = Encoding.UTF8.GetBytes("Leading slash protection");
        await provider.UploadItemAsync("/transcripts/slash-test.bundle", testData);

        string expectedFile = Path.Combine(_tempSyncDir, "transcripts", "slash-test.bundle");
        Assert.True(File.Exists(expectedFile));

        byte[]? downloaded = await provider.DownloadItemAsync("\\transcripts\\slash-test.bundle");
        Assert.NotNull(downloaded);
        Assert.Equal(testData, downloaded);
    }

    private static SqliteConnection CreateIsolatedConnection()
    {
        var conn = new SqliteConnection($"Data Source=mem_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        conn.Open();
        return conn;
    }

    [Fact]
    public async Task SyncService_SynchronizesTranscriptsBetweenTwoIsolatedRepositories()
    {
        using var conn1 = CreateIsolatedConnection();
        using var repo1 = new SqliteTranscriptRepository(conn1);

        using var conn2 = CreateIsolatedConnection();
        using var repo2 = new SqliteTranscriptRepository(conn2);

        var sync1 = new SyncService(repo1);
        var sync2 = new SyncService(repo2);

        // Populate Repo 1 with a transcript
        var transcriptId = Guid.NewGuid();
        var transcript = CreateSampleTranscript(transcriptId, "Device 1 Meeting");
        await repo1.SaveAsync(transcript);

        var options = new SyncOptions(
            ProviderType: SyncProviderType.LocalDirectory,
            TargetUriOrPath: _tempSyncDir,
            EncryptRemotePayloads: false);

        // Sync from Repo 1 -> directory
        var result1 = await sync1.SyncAsync(options);
        Assert.True(result1.Success);
        Assert.Equal(1, result1.UploadedCount);
        Assert.Equal(0, result1.DownloadedCount);

        // Sync from directory -> Repo 2
        var result2 = await sync2.SyncAsync(options);
        Assert.True(result2.Success);
        Assert.Equal(0, result2.UploadedCount);
        Assert.Equal(1, result2.DownloadedCount);

        // Verify transcript is present in Repo 2
        var syncedTranscript = await repo2.GetByIdAsync(transcriptId);
        Assert.NotNull(syncedTranscript);
        Assert.Equal("Device 1 Meeting", syncedTranscript.Title);
        Assert.Equal(transcript.Speakers.Count, syncedTranscript.Speakers.Count);
        Assert.Equal(transcript.Segments.Count, syncedTranscript.Segments.Count);
    }

    [Fact]
    public async Task SyncService_SynchronizesWithZeroKnowledgeEncryption()
    {
        using var conn1 = CreateIsolatedConnection();
        using var repo1 = new SqliteTranscriptRepository(conn1);

        using var conn2 = CreateIsolatedConnection();
        using var repo2 = new SqliteTranscriptRepository(conn2);

        var sync1 = new SyncService(repo1);
        var sync2 = new SyncService(repo2);

        var transcriptId = Guid.NewGuid();
        var transcript = CreateSampleTranscript(transcriptId, "Confidential Meeting");
        await repo1.SaveAsync(transcript);

        var options = new SyncOptions(
            ProviderType: SyncProviderType.LocalDirectory,
            TargetUriOrPath: _tempSyncDir,
            EncryptRemotePayloads: true,
            EncryptionPassphrase: "local-first-zero-cloud-aes256!");

        // Upload encrypted
        var result1 = await sync1.SyncAsync(options);
        Assert.True(result1.Success);
        Assert.Equal(1, result1.UploadedCount);

        // Ensure remote file is actually encrypted and does not contain raw text
        string bundlePath = Path.Combine(_tempSyncDir, "transcripts", $"{transcriptId}.bundle");
        Assert.True(File.Exists(bundlePath));
        byte[] rawBytes = File.ReadAllBytes(bundlePath);
        string fileContent = Encoding.UTF8.GetString(rawBytes);
        Assert.DoesNotContain("Confidential Meeting", fileContent);
        Assert.StartsWith("SCRE", fileContent);

        // Download and decrypt into Repo 2
        var result2 = await sync2.SyncAsync(options);
        Assert.True(result2.Success);
        Assert.Equal(1, result2.DownloadedCount);

        var syncedTranscript = await repo2.GetByIdAsync(transcriptId);
        Assert.NotNull(syncedTranscript);
        Assert.Equal("Confidential Meeting", syncedTranscript.Title);
    }

    [Fact]
    public async Task SyncService_VoiceprintsSync_TransfersVoiceprintsAcrossDatabases()
    {
        using var conn1 = CreateIsolatedConnection();
        using var transRepo1 = new SqliteTranscriptRepository(conn1);
        using var vpRepo1 = new SqliteVoiceprintRepository(conn1);

        using var conn2 = CreateIsolatedConnection();
        using var transRepo2 = new SqliteTranscriptRepository(conn2);
        using var vpRepo2 = new SqliteVoiceprintRepository(conn2);

        var sync1 = new SyncService(transRepo1, vpRepo1);
        var sync2 = new SyncService(transRepo2, vpRepo2);

        var vpId = Guid.NewGuid();
        var vp = new Voiceprint(vpId, "Dr. Arash", new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f }, DateTimeOffset.UtcNow);
        await vpRepo1.SaveAsync(vp);

        var options = new SyncOptions(
            ProviderType: SyncProviderType.LocalDirectory,
            TargetUriOrPath: _tempSyncDir);

        var res1 = await sync1.SyncAsync(options);
        Assert.True(res1.Success);
        Assert.Equal(1, res1.UploadedCount);

        var res2 = await sync2.SyncAsync(options);
        Assert.True(res2.Success);
        Assert.Equal(1, res2.DownloadedCount);

        var loadedVp = await vpRepo2.GetByIdAsync(vpId);
        Assert.NotNull(loadedVp);
        Assert.Equal("Dr. Arash", loadedVp.DisplayName);
        Assert.Equal(5, loadedVp.Embedding.Length);
    }

    [Fact]
    public async Task SyncService_ConcurrentConflict_CreatesConflictedCopyWithoutDataLoss()
    {
        using var conn1 = CreateIsolatedConnection();
        using var repo1 = new SqliteTranscriptRepository(conn1);

        using var conn2 = CreateIsolatedConnection();
        using var repo2 = new SqliteTranscriptRepository(conn2);

        var sync1 = new SyncService(repo1);
        var sync2 = new SyncService(repo2);

        var transcriptId = Guid.NewGuid();
        var baseTime = DateTimeOffset.UtcNow.AddHours(-2);
        var orig = CreateSampleTranscript(transcriptId, "Sprint Planning") with { CreatedAt = baseTime };

        await repo1.SaveAsync(orig);
        await repo2.SaveAsync(orig);

        var options = new SyncOptions(
            ProviderType: SyncProviderType.LocalDirectory,
            TargetUriOrPath: _tempSyncDir);

        // Repo 1 updates transcript at t+1h and syncs to remote
        var updatedRepo1 = orig with
        {
            Title = "Sprint Planning (Updated by Device 1)",
            CreatedAt = baseTime.AddHours(1)
        };
        await repo1.SaveAsync(updatedRepo1);
        var res1 = await sync1.SyncAsync(options);
        Assert.True(res1.Success);
        Assert.Equal(1, res1.UploadedCount);

        // Repo 2 concurrently edited the transcript offline (diverging content)
        var updatedRepo2 = orig with
        {
            Title = "Sprint Planning (Local Edit on Device 2)"
        };
        await repo2.SaveAsync(updatedRepo2);

        // Repo 2 syncs down remote changes: conflict is detected and branched
        var res2 = await sync2.SyncAsync(options);
        Assert.True(res2.Success);
        Assert.Equal(1, res2.ConflictCount);

        // Verify Repo 2 preserved its local copy AND saved the remote conflicted copy
        var localTranscripts = await repo2.ListAsync();
        Assert.Equal(2, localTranscripts.Count);
        Assert.Contains(localTranscripts, t => t.Title.Contains("Device 2"));
        Assert.Contains(localTranscripts, t => t.Title.Contains("Conflicted Copy"));
    }

    private static Transcript CreateSampleTranscript(Guid id, string title)
    {
        var speakers = new List<Speaker>
        {
            new("speaker-0", "Sara", "#4F46E5"),
            new("speaker-1", "Reza", "#10B981")
        };

        var words1 = new List<Word>
        {
            new("سلام", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.5), 0.98),
            new("خوش", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.0), 0.95),
            new("آمدید", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.5), 0.99)
        };

        var segments = new List<Segment>
        {
            new("speaker-0", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1.5), words1)
        };

        var audio = new AudioReference("test.wav", TimeSpan.FromSeconds(1.5), 16000);

        return new Transcript(
            id: id,
            title: title,
            createdAt: DateTimeOffset.UtcNow,
            languageCode: "fa",
            speakers: speakers,
            segments: segments,
            audio: audio);
    }
}
