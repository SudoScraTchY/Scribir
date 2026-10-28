namespace Scriber.Storage.Sync;

public enum SyncProviderType
{
    LocalDirectory,
    WebDav
}

public sealed record SyncOptions(
    SyncProviderType ProviderType,
    string TargetUriOrPath,
    string? Username = null,
    string? PasswordOrToken = null,
    bool EncryptRemotePayloads = false,
    string? EncryptionPassphrase = null);

public sealed record SyncProgress(
    string CurrentStep,
    int ItemsSynced,
    int TotalItems);

public sealed record SyncResult(
    bool Success,
    int UploadedCount,
    int DownloadedCount,
    int ConflictCount,
    string? ErrorMessage);

public sealed class SyncManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string DeviceId { get; set; } = Environment.MachineName;
    public DateTimeOffset LastModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, SyncItemRecord> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record SyncItemRecord(
    string ItemId,
    string ItemType,
    string RelativePath,
    DateTimeOffset UpdatedAtUtc,
    string Checksum,
    bool IsDeleted = false);
