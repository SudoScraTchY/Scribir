using System.Windows.Input;
using Scriber.Storage.Models;

namespace Scriber.App.ViewModels;

/// <summary>
/// Presentation wrapper for a single AI model manifest entry.
/// Tracks download progress, status, and size.
/// Implements PH1-MODEL-002.
/// </summary>
public sealed class ModelItemViewModel : BaseViewModel
{
    private ModelCacheState _state;
    private double _progressFraction;
    private long _bytesDownloaded;
    private string? _errorMessage;

    public ModelManifestEntry Entry { get; }

    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string ModelType => Entry.ModelType;
    public string License => Entry.License;
    public string SourceUrl => Entry.SourceUrl;
    public string DeviceTier => Entry.DeviceTierRecommendation;
    public long TotalBytes => Entry.SizeBytes;

    public string FormattedSize => FormatBytes(Entry.SizeBytes);

    public string FormattedLanguages => Entry.LanguageCodes.Count > 0
        ? string.Join(", ", Entry.LanguageCodes)
        : "multilingual";

    public string FormattedType => Entry.ModelType.ToLowerInvariant() switch
    {
        "asr" => "Speech Recognition",
        "diarization" => "Speaker Diarizer",
        "vad" => "Voice Activity Detection",
        _ => Entry.ModelType.ToUpperInvariant()
    };

    public string FormattedTier => Entry.DeviceTierRecommendation.ToLowerInvariant() switch
    {
        "low" => "Low-End Phone / Edge",
        "medium" => "Mid-Range Smartphone",
        "high" => "Desktop / High-End",
        _ => Entry.DeviceTierRecommendation
    };

    public string TierBadgeColor => Entry.DeviceTierRecommendation.ToLowerInvariant() switch
    {
        "low" => "#10B981",    // Emerald
        "medium" => "#F59E0B", // Amber
        "high" => "#4F46E5",   // Indigo
        _ => "#6B7280"
    };

    public ModelCacheState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(IsReady));
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanDelete));
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusBadgeColor));
            }
        }
    }

    public double ProgressFraction
    {
        get => _progressFraction;
        set
        {
            if (SetProperty(ref _progressFraction, value))
            {
                OnPropertyChanged(nameof(ProgressPercentText));
            }
        }
    }

    public long BytesDownloaded
    {
        get => _bytesDownloaded;
        set
        {
            if (SetProperty(ref _bytesDownloaded, value))
            {
                OnPropertyChanged(nameof(ProgressBytesText));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool IsDownloading => State == ModelCacheState.Downloading;
    public bool IsReady => State == ModelCacheState.Ready;
    public bool CanDownload => State is ModelCacheState.NotDownloaded or ModelCacheState.Failed;
    public bool CanDelete => State == ModelCacheState.Ready;

    public string ProgressPercentText => $"{(int)(ProgressFraction * 100)}%";
    public string ProgressBytesText => $"{FormatBytes(BytesDownloaded)} / {FormattedSize}";

    public string StatusBadgeText => State switch
    {
        ModelCacheState.Ready => "✓ Ready Offline",
        ModelCacheState.Downloading => "Downloading...",
        ModelCacheState.Failed => "Download Failed",
        _ => "Not Downloaded"
    };

    public string StatusBadgeColor => State switch
    {
        ModelCacheState.Ready => "#10B981",       // Success
        ModelCacheState.Downloading => "#3B82F6", // Blue
        ModelCacheState.Failed => "#EF4444",      // Danger
        _ => "#94A3B8"                            // Slate
    };

    public ModelItemViewModel(ModelManifestEntry entry, ModelStatus initialStatus)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        UpdateStatus(initialStatus);
    }

    public void UpdateStatus(ModelStatus status)
    {
        State = status.State;
        ProgressFraction = status.ProgressFraction;
        BytesDownloaded = status.BytesDownloaded;
        ErrorMessage = status.ErrorMessage;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
