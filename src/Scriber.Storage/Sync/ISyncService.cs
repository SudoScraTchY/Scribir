namespace Scriber.Storage.Sync;

/// <summary>
/// Orchestrates decentralized, zero-cloud synchronization of transcripts and voiceprints (PH2.5-SYNC-001, ADR-0007).
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// Executes a bidirectional synchronization pass using the configured remote provider and encryption settings.
    /// </summary>
    Task<SyncResult> SyncAsync(SyncOptions options, IProgress<SyncProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Verifies access, connectivity, and authentication against the target sync endpoint or folder.
    /// </summary>
    Task<bool> TestConnectionAsync(SyncOptions options, CancellationToken ct = default);
}
