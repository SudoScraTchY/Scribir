using System.Net;
using System.Security.Cryptography;
using System.Text;
using Scriber.Storage.Models;
using Xunit;

namespace Scriber.Storage.Tests;

public class LocalModelCacheManagerTests : IDisposable
{
    private readonly string _tempCacheDir;

    public LocalModelCacheManagerTests()
    {
        _tempCacheDir = Path.Combine(Path.GetTempPath(), "scriber_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempCacheDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempCacheDir))
        {
            try { Directory.Delete(_tempCacheDir, recursive: true); } catch { }
        }
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }

    private static (ModelManifestEntry Entry, byte[] File1Bytes, byte[] File2Bytes) CreateTestModelEntry()
    {
        byte[] file1 = Encoding.UTF8.GetBytes("test model weight content 1");
        byte[] file2 = Encoding.UTF8.GetBytes("test model token dictionary 2");

        string sha1 = Convert.ToHexStringLower(SHA256.HashData(file1));
        string sha2 = Convert.ToHexStringLower(SHA256.HashData(file2));

        var entry = new ModelManifestEntry
        {
            Id = "test-mini-model",
            Name = "Test Mini Model",
            ModelType = "asr",
            LanguageCodes = ["en"],
            DeviceTierRecommendation = "low",
            SizeBytes = file1.Length + file2.Length,
            QuantVariant = "int8",
            License = "MIT",
            SourceUrl = "https://huggingface.co/test/mini-model",
            DateAdded = "2026-10-15",
            Files =
            [
                new()
                {
                    Filename = "model.onnx",
                    Url = "https://huggingface.co/test/mini-model/resolve/main/model.onnx",
                    Sha256 = sha1,
                    SizeBytes = file1.Length
                },
                new()
                {
                    Filename = "tokens.txt",
                    Url = "https://huggingface.co/test/mini-model/resolve/main/tokens.txt",
                    Sha256 = sha2,
                    SizeBytes = file2.Length
                }
            ]
        };

        return (entry, file1, file2);
    }

    [Fact]
    public async Task DownloadModelAsync_ValidChecksums_ReturnsReadyAndProgress()
    {
        var (entry, file1, file2) = CreateTestModelEntry();

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsoluteUri.EndsWith("model.onnx"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file1) };
            if (req.RequestUri!.AbsoluteUri.EndsWith("tokens.txt"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file2) };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(mockHandler);
        var manager = new LocalModelCacheManager(_tempCacheDir, client);

        // Before download
        var initialStatus = manager.GetStatus(entry);
        Assert.Equal(ModelCacheState.NotDownloaded, initialStatus.State);

        var progressReports = new List<ModelStatus>();
        var progress = new Progress<ModelStatus>(p => progressReports.Add(p));

        // Perform download
        var finalStatus = await manager.DownloadModelAsync(entry, progress);

        Assert.Equal(ModelCacheState.Ready, finalStatus.State);
        Assert.NotNull(finalStatus.LocalDirectory);
        Assert.True(Directory.Exists(finalStatus.LocalDirectory));
        Assert.True(File.Exists(Path.Combine(finalStatus.LocalDirectory, "model.onnx")));
        Assert.True(File.Exists(Path.Combine(finalStatus.LocalDirectory, "tokens.txt")));

        // Status query after download
        var readyStatus = manager.GetStatus(entry);
        Assert.Equal(ModelCacheState.Ready, readyStatus.State);

        // Progress reports were observed
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => p.State == ModelCacheState.Downloading);
    }

    [Fact]
    public async Task DownloadModelAsync_BadChecksum_FailsAndCleansUp()
    {
        var (entry, _, file2) = CreateTestModelEntry();
        byte[] corruptedFile1 = Encoding.UTF8.GetBytes("corrupted payload");

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsoluteUri.EndsWith("model.onnx"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(corruptedFile1) };
            if (req.RequestUri!.AbsoluteUri.EndsWith("tokens.txt"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file2) };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(mockHandler);
        var manager = new LocalModelCacheManager(_tempCacheDir, client);

        var finalStatus = await manager.DownloadModelAsync(entry);

        Assert.Equal(ModelCacheState.Failed, finalStatus.State);
        Assert.Contains("Checksum mismatch", finalStatus.ErrorMessage);
        Assert.Contains("ADR-0009", finalStatus.ErrorMessage);

        // Corrupted file was not moved into place
        string modelDir = Path.Combine(_tempCacheDir, entry.Id);
        Assert.False(File.Exists(Path.Combine(modelDir, "model.onnx")));
    }

    [Fact]
    public async Task DeleteModel_FreesFilesAndResetsStateToNotDownloaded()
    {
        var (entry, file1, file2) = CreateTestModelEntry();

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsoluteUri.EndsWith("model.onnx"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file1) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file2) };
        });

        using var client = new HttpClient(mockHandler);
        var manager = new LocalModelCacheManager(_tempCacheDir, client);

        await manager.DownloadModelAsync(entry);
        Assert.Equal(ModelCacheState.Ready, manager.GetStatus(entry).State);

        // Delete
        bool deleted = manager.DeleteModel(entry);
        Assert.True(deleted);

        // State is reset to NotDownloaded
        Assert.Equal(ModelCacheState.NotDownloaded, manager.GetStatus(entry).State);
        Assert.False(Directory.Exists(Path.Combine(_tempCacheDir, entry.Id)));
    }
}
