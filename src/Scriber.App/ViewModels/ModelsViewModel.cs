using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriber.Storage.Models;

namespace Scriber.App.ViewModels;

/// <summary>
/// ViewModel for browsing, downloading, verifying, and deleting on-device AI models.
/// Implements PH1-MODEL-002 per ADR-0009.
/// </summary>
public sealed class ModelsViewModel : BaseViewModel
{
    private readonly IModelCacheManager _cacheManager;
    private readonly IModelCatalogService _catalogService;

    private readonly List<ModelItemViewModel> _allModels = [];
    private string _searchQuery = string.Empty;
    private string _selectedLanguageFilter = "All";
    private string _selectedTierFilter = "All";
    private string _selectedTypeFilter = "All";
    private bool _isLoading;
    private string _summaryText = string.Empty;

    public ObservableCollection<ModelItemViewModel> FilteredModels { get; } = [];

    public IReadOnlyList<string> LanguageFilterOptions { get; } = ["All", "en", "fa", "multilingual"];
    public IReadOnlyList<string> TierFilterOptions { get; } = ["All", "low", "medium", "high"];
    public IReadOnlyList<string> TypeFilterOptions { get; } = ["All", "asr", "diarization", "vad"];

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedLanguageFilter
    {
        get => _selectedLanguageFilter;
        set
        {
            if (SetProperty(ref _selectedLanguageFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedTierFilter
    {
        get => _selectedTierFilter;
        set
        {
            if (SetProperty(ref _selectedTierFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set
        {
            if (SetProperty(ref _selectedTypeFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    public string CacheDirectory => _cacheManager.CacheDirectory;

    public ICommand DownloadModelCommand { get; }
    public ICommand DeleteModelCommand { get; }
    public ICommand RefreshStatusCommand { get; }
    public ICommand OpenSourceUrlCommand { get; }

    public ModelsViewModel(IModelCacheManager cacheManager, IModelCatalogService catalogService)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));

        DownloadModelCommand = new Command<ModelItemViewModel>(async m => await DownloadModelAsync(m));
        DeleteModelCommand = new Command<ModelItemViewModel>(async m => await DeleteModelAsync(m));
        RefreshStatusCommand = new Command(RefreshAllStatuses);
        OpenSourceUrlCommand = new Command<ModelItemViewModel>(async m => await OpenSourceUrlAsync(m));

        LoadCatalog();
    }

    public void LoadCatalog()
    {
        IsLoading = true;
        try
        {
            _allModels.Clear();
            var catalog = _catalogService.GetCatalog();

            foreach (var entry in catalog.Models)
            {
                var status = _cacheManager.GetStatus(entry);
                var item = new ModelItemViewModel(entry, status);
                _allModels.Add(item);
            }

            ApplyFilters();
            UpdateSummary();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void RefreshAllStatuses()
    {
        foreach (var item in _allModels)
        {
            var status = _cacheManager.GetStatus(item.Entry);
            item.UpdateStatus(status);
        }
        UpdateSummary();
    }

    private void ApplyFilters()
    {
        var filtered = _allModels.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim().ToLowerInvariant();
            filtered = filtered.Where(m =>
                m.Name.ToLowerInvariant().Contains(q) ||
                m.Description.ToLowerInvariant().Contains(q) ||
                m.Id.ToLowerInvariant().Contains(q));
        }

        if (SelectedLanguageFilter != "All")
        {
            filtered = filtered.Where(m =>
                m.Entry.LanguageCodes.Contains(SelectedLanguageFilter, StringComparer.OrdinalIgnoreCase));
        }

        if (SelectedTierFilter != "All")
        {
            filtered = filtered.Where(m =>
                string.Equals(m.DeviceTier, SelectedTierFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedTypeFilter != "All")
        {
            filtered = filtered.Where(m =>
                string.Equals(m.ModelType, SelectedTypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        FilteredModels.Clear();
        foreach (var item in filtered)
        {
            FilteredModels.Add(item);
        }
    }

    private void UpdateSummary()
    {
        int readyCount = _allModels.Count(m => m.IsReady);
        long totalBytes = _allModels.Where(m => m.IsReady).Sum(m => m.TotalBytes);
        SummaryText = $"{readyCount} of {_allModels.Count} models installed offline ({FormatBytes(totalBytes)})";
    }

    private async Task DownloadModelAsync(ModelItemViewModel? item)
    {
        if (item == null || !item.CanDownload) return;

        // Visual notice of network access
        bool proceed = await Shell.Current.DisplayAlertAsync(
            "Download Model Weights",
            $"Scribir will download '{item.Name}' ({item.FormattedSize}) directly from:\n\n{item.SourceUrl}\n\nNotice: This requires internet access. After download completes, all transcription will run 100% locally and offline.",
            "Download",
            "Cancel");

        if (!proceed) return;

        item.State = ModelCacheState.Downloading;
        item.ProgressFraction = 0.0;
        item.BytesDownloaded = 0;
        item.ErrorMessage = null;

        var progress = new Progress<ModelStatus>(status =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                item.UpdateStatus(status);
                UpdateSummary();
            });
        });

        try
        {
            var finalStatus = await _cacheManager.DownloadModelAsync(item.Entry, progress);
            item.UpdateStatus(finalStatus);
            UpdateSummary();

            if (finalStatus.State == ModelCacheState.Ready)
            {
                await Shell.Current.DisplayAlertAsync("Model Ready", $"'{item.Name}' downloaded and verified successfully! It is now available for offline use.", "OK");
            }
            else if (finalStatus.State == ModelCacheState.Failed)
            {
                await Shell.Current.DisplayAlertAsync("Download Failed", finalStatus.ErrorMessage ?? "Checksum or network error.", "OK");
            }
        }
        catch (Exception ex)
        {
            item.State = ModelCacheState.Failed;
            item.ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlertAsync("Download Error", ex.Message, "OK");
        }
    }

    private async Task DeleteModelAsync(ModelItemViewModel? item)
    {
        if (item == null || !item.CanDelete) return;

        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Model Weights",
            $"Are you sure you want to delete '{item.Name}' ({item.FormattedSize}) from local storage?\n\nYou will need to download it again to use this model.",
            "Delete",
            "Cancel");

        if (!confirm) return;

        bool deleted = _cacheManager.DeleteModel(item.Entry);
        if (deleted)
        {
            item.State = ModelCacheState.NotDownloaded;
            item.ProgressFraction = 0.0;
            item.BytesDownloaded = 0;
            UpdateSummary();
            await Shell.Current.DisplayAlertAsync("Deleted", $"'{item.Name}' was deleted from local storage.", "OK");
        }
        else
        {
            await Shell.Current.DisplayAlertAsync("Delete Failed", "Could not remove model directory from disk.", "OK");
        }
    }

    private static async Task OpenSourceUrlAsync(ModelItemViewModel? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.SourceUrl)) return;
        try
        {
            await Browser.Default.OpenAsync(new Uri(item.SourceUrl), BrowserLaunchMode.SystemPreferred);
        }
        catch { }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
