namespace Scriber.Storage.Sync;

using System.Text.Json;

/// <summary>
/// Sync provider targeting a local or mounted directory (Syncthing, shared NAS, external drive, Nextcloud local client).
/// </summary>
public sealed class LocalFolderSyncProvider : ISyncProvider
{
    private readonly string _baseDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LocalFolderSyncProvider(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new ArgumentException("Base directory path cannot be empty.", nameof(baseDirectory));
        }
        _baseDirectory = baseDirectory;
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(_baseDirectory))
            {
                Directory.CreateDirectory(_baseDirectory);
            }

            string probeFile = Path.Combine(_baseDirectory, ".probe_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probeFile, "ok");
            File.Delete(probeFile);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task<SyncManifest?> GetRemoteManifestAsync(CancellationToken ct = default)
    {
        string manifestPath = Path.Combine(_baseDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(manifestPath, ct);
            return JsonSerializer.Deserialize<SyncManifest>(bytes);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveRemoteManifestAsync(SyncManifest manifest, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Directory.CreateDirectory(_baseDirectory);
        string manifestPath = Path.Combine(_baseDirectory, "manifest.json");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        await File.WriteAllBytesAsync(manifestPath, bytes, ct);
    }

    public async Task<byte[]?> DownloadItemAsync(string relativePath, CancellationToken ct = default)
    {
        string fullPath = GetFullPath(relativePath);
        if (!File.Exists(fullPath)) return null;

        return await File.ReadAllBytesAsync(fullPath, ct);
    }

    public async Task UploadItemAsync(string relativePath, byte[] data, CancellationToken ct = default)
    {
        string fullPath = GetFullPath(relativePath);
        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await File.WriteAllBytesAsync(fullPath, data, ct);
    }

    public Task DeleteItemAsync(string relativePath, CancellationToken ct = default)
    {
        string fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        return Task.CompletedTask;
    }

    private string GetFullPath(string relativePath)
    {
        string cleanRelative = relativePath.TrimStart('/', '\\')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(_baseDirectory, cleanRelative);
    }

    public void Dispose()
    {
    }
}
