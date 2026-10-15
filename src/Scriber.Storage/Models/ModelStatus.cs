namespace Scriber.Storage.Models;

/// <summary>
/// Lifecycle state of a local model according to PH1-STORE-002.
/// </summary>
public enum ModelCacheState
{
    NotDownloaded,
    Downloading,
    Ready,
    Failed
}

/// <summary>
/// Status report of a model's local cache availability and download progress.
/// </summary>
public record ModelStatus(
    string ModelId,
    ModelCacheState State,
    double ProgressFraction = 0.0,
    long BytesDownloaded = 0,
    long TotalBytes = 0,
    string? ErrorMessage = null,
    string? LocalDirectory = null);
