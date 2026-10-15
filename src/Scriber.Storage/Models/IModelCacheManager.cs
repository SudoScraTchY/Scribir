namespace Scriber.Storage.Models;

/// <summary>
/// Contract for managing downloading, checksum verification, status tracking, and deletion of local models.
/// Implements PH1-STORE-002 per ADR-0009.
/// </summary>
public interface IModelCacheManager
{
    string CacheDirectory { get; }

    ModelStatus GetStatus(ModelManifestEntry model);

    Task<ModelStatus> DownloadModelAsync(
        ModelManifestEntry model,
        IProgress<ModelStatus>? progress = null,
        CancellationToken ct = default);

    bool DeleteModel(ModelManifestEntry model);
}
