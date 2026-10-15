using System.Security.Cryptography;

namespace Scriber.Storage.Models;

/// <summary>
/// Manages downloading, verifying SHA-256 checksums, and caching model files locally.
/// Strictly enforces ADR-0009: downloads occur directly from upstream sources without proxying.
/// Implements PH1-STORE-002.
/// </summary>
public sealed class LocalModelCacheManager : IModelCacheManager, IDisposable
{
    private readonly string _cacheDirectory;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public string CacheDirectory => _cacheDirectory;

    public LocalModelCacheManager(string cacheDirectory, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsClient = true;
        }
    }

    public ModelStatus GetStatus(ModelManifestEntry model)
    {
        ArgumentNullException.ThrowIfNull(model);

        string modelDir = Path.Combine(_cacheDirectory, model.Id);
        if (!Directory.Exists(modelDir))
        {
            return new ModelStatus(model.Id, ModelCacheState.NotDownloaded);
        }

        if (model.Files == null || model.Files.Count == 0)
        {
            return new ModelStatus(model.Id, ModelCacheState.NotDownloaded);
        }

        foreach (var file in model.Files)
        {
            string filePath = Path.Combine(modelDir, file.Filename);
            if (!File.Exists(filePath))
            {
                return new ModelStatus(model.Id, ModelCacheState.NotDownloaded);
            }

            // Verify checksum
            string computedHash = ComputeSha256(filePath);
            if (!computedHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ModelStatus(
                    model.Id,
                    ModelCacheState.Failed,
                    ErrorMessage: $"Corrupt file '{file.Filename}': checksum mismatch. Expected {file.Sha256}, got {computedHash}");
            }
        }

        return new ModelStatus(
            model.Id,
            ModelCacheState.Ready,
            ProgressFraction: 1.0,
            BytesDownloaded: model.SizeBytes,
            TotalBytes: model.SizeBytes,
            LocalDirectory: modelDir);
    }

    public async Task<ModelStatus> DownloadModelAsync(
        ModelManifestEntry model,
        IProgress<ModelStatus>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ModelManifestValidator.ValidateEntry(model);

        string modelDir = Path.Combine(_cacheDirectory, model.Id);
        if (!Directory.Exists(modelDir))
        {
            Directory.CreateDirectory(modelDir);
        }

        // Check if already ready
        var currentStatus = GetStatus(model);
        if (currentStatus.State == ModelCacheState.Ready)
        {
            progress?.Report(currentStatus);
            return currentStatus;
        }

        long totalBytesExpected = model.Files.Sum(f => f.SizeBytes > 0 ? f.SizeBytes : 0);
        if (totalBytesExpected <= 0) totalBytesExpected = model.SizeBytes;

        long totalDownloadedSoFar = 0;

        var initialDownloading = new ModelStatus(
            model.Id,
            ModelCacheState.Downloading,
            ProgressFraction: 0.0,
            BytesDownloaded: 0,
            TotalBytes: totalBytesExpected);
        progress?.Report(initialDownloading);

        foreach (var file in model.Files)
        {
            ct.ThrowIfCancellationRequested();

            string targetPath = Path.Combine(modelDir, file.Filename);
            string tempPath = targetPath + ".download.tmp";

            try
            {
                // Check if existing file is already valid
                if (File.Exists(targetPath) && ComputeSha256(targetPath).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    totalDownloadedSoFar += new FileInfo(targetPath).Length;
                    continue;
                }

                // Download to temporary file
                using (var response = await _httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    await using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            totalDownloadedSoFar += read;

                            double fraction = totalBytesExpected > 0
                                ? Math.Clamp((double)totalDownloadedSoFar / totalBytesExpected, 0.0, 0.99)
                                : 0.5;

                            progress?.Report(new ModelStatus(
                                model.Id,
                                ModelCacheState.Downloading,
                                ProgressFraction: fraction,
                                BytesDownloaded: totalDownloadedSoFar,
                                TotalBytes: totalBytesExpected));
                        }
                    }
                }

                // Verify SHA-256 Checksum
                string actualSha = ComputeSha256(tempPath);
                if (!actualSha.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);

                    var failureStatus = new ModelStatus(
                        model.Id,
                        ModelCacheState.Failed,
                        ErrorMessage: $"Checksum mismatch for '{file.Filename}'. Expected {file.Sha256}, got {actualSha}. Download rejected per ADR-0009.");
                    progress?.Report(failureStatus);
                    return failureStatus;
                }

                // Move verified file to target
                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);

                var errorStatus = new ModelStatus(
                    model.Id,
                    ModelCacheState.Failed,
                    ErrorMessage: $"Failed to download '{file.Filename}': {ex.Message}");
                progress?.Report(errorStatus);
                return errorStatus;
            }
        }

        var readyStatus = new ModelStatus(
            model.Id,
            ModelCacheState.Ready,
            ProgressFraction: 1.0,
            BytesDownloaded: totalBytesExpected,
            TotalBytes: totalBytesExpected,
            LocalDirectory: modelDir);

        progress?.Report(readyStatus);
        return readyStatus;
    }

    public bool DeleteModel(ModelManifestEntry model)
    {
        ArgumentNullException.ThrowIfNull(model);

        string modelDir = Path.Combine(_cacheDirectory, model.Id);
        if (Directory.Exists(modelDir))
        {
            Directory.Delete(modelDir, recursive: true);
            return true;
        }

        return false;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
