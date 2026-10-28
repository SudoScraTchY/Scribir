namespace Scriber.Storage.Sync;

/// <summary>
/// Low-level abstraction over remote sync storage backends (Local folder, WebDAV).
/// </summary>
public interface ISyncProvider : IDisposable
{
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
    Task<SyncManifest?> GetRemoteManifestAsync(CancellationToken ct = default);
    Task SaveRemoteManifestAsync(SyncManifest manifest, CancellationToken ct = default);
    Task<byte[]?> DownloadItemAsync(string relativePath, CancellationToken ct = default);
    Task UploadItemAsync(string relativePath, byte[] data, CancellationToken ct = default);
    Task DeleteItemAsync(string relativePath, CancellationToken ct = default);
}
