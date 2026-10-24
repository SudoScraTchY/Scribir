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
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromHours(2)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Scribir/1.0");
            _ownsClient = true;
        }
    }

    public ModelStatus GetStatus(ModelManifestEntry model)
    {
        ArgumentNullException.ThrowIfNull(model);

        string modelDir = Path.Combine(_cacheDirectory, model.Id);
        if (!Directory.Exists(modelDir))
        {
            TrySeedFromRepoFallback(model, modelDir);
            if (!Directory.Exists(modelDir))
            {
                return new ModelStatus(model.Id, ModelCacheState.NotDownloaded);
            }
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
                // Try to seed individual missing file
                TrySeedIndividualFile(modelDir, file);
                if (!File.Exists(filePath))
                {
                    return new ModelStatus(model.Id, ModelCacheState.NotDownloaded);
                }
            }

            // Verify checksum
            string computedHash = ComputeSha256(filePath).Trim();
            string expectedHash = file.Sha256.Trim();
            if (!computedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
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
                if (File.Exists(targetPath) && ComputeSha256(targetPath).Trim().Equals(file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    totalDownloadedSoFar += new FileInfo(targetPath).Length;
                    continue;
                }

                // Check if available from local repo fallback
                string? repoFallback = FindRepoFallbackFile(file.Filename);
                if (!string.IsNullOrEmpty(repoFallback) && File.Exists(repoFallback))
                {
                    string fallbackSha = ComputeSha256(repoFallback).Trim();
                    if (fallbackSha.Equals(file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(repoFallback, targetPath, overwrite: true);
                        if (file.Filename == "nemotron3_stream_step.onnx")
                        {
                            CopyDiarizerPortAssets(modelDir);
                        }
                        totalDownloadedSoFar += new FileInfo(targetPath).Length;
                        continue;
                    }
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
                string actualSha = ComputeSha256(tempPath).Trim().ToLowerInvariant();
                string expectedSha = file.Sha256.Trim().ToLowerInvariant();
                if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);

                    var failureStatus = new ModelStatus(
                        model.Id,
                        ModelCacheState.Failed,
                        ErrorMessage: $"Checksum mismatch for '{file.Filename}'. Expected {expectedSha}, got {actualSha}. Download rejected per ADR-0009.");
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
            foreach (var key in HashCache.Keys.Where(k => k.Contains(model.Id)))
            {
                HashCache.TryRemove(key, out _);
            }
            return true;
        }

        return false;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, DateTime LastWriteTimeUtc, string Hash)> HashCache = new();

    private static string ComputeSha256(string filePath)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists) return string.Empty;

        if (HashCache.TryGetValue(filePath, out var cached) &&
            cached.Length == fi.Length &&
            cached.LastWriteTimeUtc == fi.LastWriteTimeUtc)
        {
            return cached.Hash;
        }

        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        string result = Convert.ToHexStringLower(hash);

        HashCache[filePath] = (fi.Length, fi.LastWriteTimeUtc, result);
        return result;
    }

    private static string? FindRepoFallbackFile(string filename)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;

        if (dir == null) return null;
        string root = dir.FullName;

        string[] candidatePaths =
        [
            Path.Combine(root, "spikes", "PH0-SPIKE-001", "artifacts", "onnx", filename),
            Path.Combine(root, "spikes", "PH0-SPIKE-001", "artifacts", "sherpa-models", filename),
            Path.Combine(root, "spikes", "PH0-SPIKE-001", "artifacts", filename),
            Path.Combine(root, "spikes", "PH0-SPIKE-002", "artifacts", "whisper-export", filename),
            Path.Combine(root, "spikes", "PH0-SPIKE-002", "artifacts", filename),
            Path.Combine(root, "src", "Scriber.Pipeline.Tests", "fixtures", filename)
        ];

        return candidatePaths.FirstOrDefault(File.Exists);
    }

    private static void TrySeedFromRepoFallback(ModelManifestEntry model, string modelDir)
    {
        if (model.Files == null || model.Files.Count == 0) return;

        bool allAvailable = true;
        foreach (var file in model.Files)
        {
            string? fallback = FindRepoFallbackFile(file.Filename);
            if (string.IsNullOrEmpty(fallback) || !File.Exists(fallback))
            {
                allAvailable = false;
                break;
            }

            string sha = ComputeSha256(fallback).Trim();
            if (!sha.Equals(file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                allAvailable = false;
                break;
            }
        }

        if (allAvailable)
        {
            if (!Directory.Exists(modelDir)) Directory.CreateDirectory(modelDir);
            foreach (var file in model.Files)
            {
                string fallback = FindRepoFallbackFile(file.Filename)!;
                string dest = Path.Combine(modelDir, file.Filename);
                File.Copy(fallback, dest, overwrite: true);
            }

            if (model.Id == "nemotron3-diarizer-v3")
            {
                CopyDiarizerPortAssets(modelDir);
            }
        }
    }

    private static void TrySeedIndividualFile(string modelDir, ModelFileEntry file)
    {
        string? fallback = FindRepoFallbackFile(file.Filename);
        if (!string.IsNullOrEmpty(fallback) && File.Exists(fallback))
        {
            string sha = ComputeSha256(fallback).Trim();
            if (sha.Equals(file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(modelDir)) Directory.CreateDirectory(modelDir);
                string dest = Path.Combine(modelDir, file.Filename);
                File.Copy(fallback, dest, overwrite: true);
                if (file.Filename == "nemotron3_stream_step.onnx")
                {
                    CopyDiarizerPortAssets(modelDir);
                }
            }
        }
    }

    private static void CopyDiarizerPortAssets(string modelDir)
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
                dir = dir.Parent;

            if (dir == null) return;
            string portAssetsDir = Path.Combine(dir.FullName, "spikes", "PH0-SPIKE-001", "artifacts", "csharp-port-assets");
            if (!Directory.Exists(portAssetsDir)) return;

            string[] assets =
            [
                "embedder_projection_weight_512x1024.bin",
                "mel_filters_128x257.bin",
                "silence_embeds_512.bin",
                "window_hann_400.bin"
            ];

            foreach (var asset in assets)
            {
                string src = Path.Combine(portAssetsDir, asset);
                string dst = Path.Combine(modelDir, asset);
                if (File.Exists(src) && !File.Exists(dst))
                {
                    File.Copy(src, dst, overwrite: true);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
