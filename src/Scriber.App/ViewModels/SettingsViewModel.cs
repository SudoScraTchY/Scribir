namespace Scriber.App.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Scriber.Core.Domain;
using Scriber.Core.Inference;
using Scriber.Providers;
using Scriber.Storage;
using Scriber.Storage.Models;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly IModelCacheManager _cacheManager;
    private readonly IModelCatalogService _catalogService;
    private readonly IVoiceprintRepository _voiceprintRepo;
    private readonly IProviderSettingsStore _providerSettingsStore;
    private readonly ICompletionProvider _completionProvider;

    private bool _isLoadingVoiceprints;
    private string _baseUrl = string.Empty;
    private string _apiKey = string.Empty;
    private string _modelName = string.Empty;
    private int _timeoutSeconds = 60;
    private string _connectionStatus = string.Empty;
    private bool _isTestingConnection;
    private bool _isSavingProviderSettings;
    private double _confidenceThreshold = 0.80;

    // Pipeline Engine Settings (Moved from Transcribe Tab per User Directives)
    private ModelSelectorItem? _selectedAsrModel;
    private LanguageItem _selectedLanguageItem;
    private bool _enableVad = true;
    private string _selectedVadEngine = "Silero VAD v5 (Neural Filter)";
    private bool _enableDiarization = true;
    private string _selectedDiarizerEngine = "Nemotron-3 Streaming Diarizer";
    private string _selectedSpeakerCount = "Auto";
    private bool _enableVoiceprints = true;
    private bool _saveRawAudio = true;

    public string AppName => "Scribir";
    public string Version => "1.0.0 (Phase 2 - Offline Native)";
    public string CacheDirectory => _cacheManager.CacheDirectory;
    public string DatabasePath { get; }
    public string ArchitectureNotes => "Offline-first, on-device AI transcription & cross-session speaker identity powered by ONNX Runtime, Whisper.net, and llama.cpp.";

    // =========================================================================
    // 1. PIPELINE ENGINE SELECTION PROPERTIES (ASR, VAD, DIARIZATION, LANG)
    // =========================================================================

    public ObservableCollection<ModelSelectorItem> AvailableAsrModels { get; } = [];
    public bool HasDownloadedModels => AvailableAsrModels.Count > 0;
    public bool HasNoDownloadedModels => AvailableAsrModels.Count == 0;

    public ModelSelectorItem? SelectedAsrModel
    {
        get => _selectedAsrModel;
        set
        {
            if (SetProperty(ref _selectedAsrModel, value) && value != null)
            {
                Preferences.Default.Set("DefaultAsrModelId", value.Id);
            }
        }
    }

    public IReadOnlyList<LanguageItem> AvailableLanguages => TranscribeViewModel.SupportedLanguages;

    public LanguageItem SelectedLanguageItem
    {
        get => _selectedLanguageItem;
        set
        {
            if (SetProperty(ref _selectedLanguageItem, value) && value != null)
            {
                Preferences.Default.Set("DefaultLanguageCode", value.Code);
            }
        }
    }

    public bool EnableVad
    {
        get => _enableVad;
        set
        {
            if (SetProperty(ref _enableVad, value))
            {
                Preferences.Default.Set("EnableVad", value);
            }
        }
    }

    public IReadOnlyList<string> AvailableVadEngines { get; } =
    [
        "Silero VAD v5 (Neural Filter)",
        "Energy-Based (RMS Threshold)",
        "Disabled (Raw Stream)"
    ];

    public string SelectedVadEngine
    {
        get => _selectedVadEngine;
        set
        {
            if (SetProperty(ref _selectedVadEngine, value))
            {
                Preferences.Default.Set("DefaultVadEngine", value);
            }
        }
    }

    public bool EnableDiarization
    {
        get => _enableDiarization;
        set
        {
            if (SetProperty(ref _enableDiarization, value))
            {
                Preferences.Default.Set("EnableDiarization", value);
            }
        }
    }

    public IReadOnlyList<string> AvailableDiarizerEngines { get; } =
    [
        "Nemotron-3 Streaming Diarizer",
        "Passthrough / Single Speaker"
    ];

    public string SelectedDiarizerEngine
    {
        get => _selectedDiarizerEngine;
        set
        {
            if (SetProperty(ref _selectedDiarizerEngine, value))
            {
                Preferences.Default.Set("DefaultDiarizerEngine", value);
            }
        }
    }

    public IReadOnlyList<string> SpeakerCountOptions { get; } = ["Auto", "1", "2", "3", "4", "5", "6"];

    public string SelectedSpeakerCount
    {
        get => _selectedSpeakerCount;
        set
        {
            if (SetProperty(ref _selectedSpeakerCount, value))
            {
                Preferences.Default.Set("DefaultSpeakerCount", value);
            }
        }
    }

    public bool EnableVoiceprints
    {
        get => _enableVoiceprints;
        set
        {
            if (SetProperty(ref _enableVoiceprints, value))
            {
                Preferences.Default.Set("EnableVoiceprints", value);
            }
        }
    }

    public bool SaveRawAudio
    {
        get => _saveRawAudio;
        set
        {
            if (SetProperty(ref _saveRawAudio, value))
            {
                Preferences.Default.Set("SaveRawAudio", value);
            }
        }
    }

    // =========================================================================
    // 2. REVIEW & CONFIDENCE PROPERTIES
    // =========================================================================

    public double ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set
        {
            double rounded = Math.Round(value, 2);
            if (SetProperty(ref _confidenceThreshold, rounded))
            {
                Preferences.Default.Set("ConfidenceThreshold", _confidenceThreshold);
                OnPropertyChanged(nameof(ConfidenceThresholdText));
            }
        }
    }

    public string ConfidenceThresholdText => $"{(int)(_confidenceThreshold * 100)}%";

    // =========================================================================
    // 3. BYOK PROVIDER PROPERTIES
    // =========================================================================

    public string BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    public string ModelName
    {
        get => _modelName;
        set => SetProperty(ref _modelName, value);
    }

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => SetProperty(ref _timeoutSeconds, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        set => SetProperty(ref _isTestingConnection, value);
    }

    public bool IsSavingProviderSettings
    {
        get => _isSavingProviderSettings;
        set => SetProperty(ref _isSavingProviderSettings, value);
    }

    public ObservableCollection<Voiceprint> EnrolledVoiceprints { get; } = [];

    public bool IsLoadingVoiceprints
    {
        get => _isLoadingVoiceprints;
        set => SetProperty(ref _isLoadingVoiceprints, value);
    }

    public ICommand LoadVoiceprintsCommand { get; }
    public ICommand RenameVoiceprintCommand { get; }
    public ICommand DeleteVoiceprintCommand { get; }
    public ICommand MergeVoiceprintCommand { get; }
    public ICommand SaveProviderSettingsCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand OpenModelsCommand { get; }

    public SettingsViewModel(
        IModelCacheManager cacheManager,
        IModelCatalogService catalogService,
        IVoiceprintRepository voiceprintRepo,
        IProviderSettingsStore providerSettingsStore,
        ICompletionProvider completionProvider,
        string databasePath)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        _voiceprintRepo = voiceprintRepo ?? throw new ArgumentNullException(nameof(voiceprintRepo));
        _providerSettingsStore = providerSettingsStore ?? throw new ArgumentNullException(nameof(providerSettingsStore));
        _completionProvider = completionProvider ?? throw new ArgumentNullException(nameof(completionProvider));
        DatabasePath = databasePath;

        // Initialize persistent engine preferences
        _confidenceThreshold = Preferences.Default.Get("ConfidenceThreshold", 0.80);
        _enableVad = Preferences.Default.Get("EnableVad", true);
        _selectedVadEngine = Preferences.Default.Get("DefaultVadEngine", "Silero VAD v5 (Neural Filter)");
        _enableDiarization = Preferences.Default.Get("EnableDiarization", true);
        _selectedDiarizerEngine = Preferences.Default.Get("DefaultDiarizerEngine", "Nemotron-3 Streaming Diarizer");
        _selectedSpeakerCount = Preferences.Default.Get("DefaultSpeakerCount", "Auto");
        _enableVoiceprints = Preferences.Default.Get("EnableVoiceprints", true);
        _saveRawAudio = Preferences.Default.Get("SaveRawAudio", true);

        string savedLang = Preferences.Default.Get("DefaultLanguageCode", "auto");
        _selectedLanguageItem = TranscribeViewModel.SupportedLanguages.FirstOrDefault(l => l.Code == savedLang)
            ?? TranscribeViewModel.SupportedLanguages[0];

        LoadVoiceprintsCommand = new Command(async () => await LoadVoiceprintsAsync());
        RenameVoiceprintCommand = new Command<Voiceprint>(async vp => await RenameVoiceprintAsync(vp));
        DeleteVoiceprintCommand = new Command<Voiceprint>(async vp => await DeleteVoiceprintAsync(vp));
        MergeVoiceprintCommand = new Command<Voiceprint>(async vp => await MergeVoiceprintAsync(vp));
        SaveProviderSettingsCommand = new Command(async () => await SaveProviderSettingsAsync());
        TestConnectionCommand = new Command(async () => await TestConnectionAsync());
        OpenModelsCommand = new Command(async () => await Shell.Current.GoToAsync("//models"));

        Task.Run(InitializeAsync);
    }

    private async Task InitializeAsync()
    {
        await LoadPipelineModelsAsync();
        await LoadProviderSettingsAsync();
        await LoadVoiceprintsAsync();
    }

    public async Task LoadPipelineModelsAsync()
    {
        try
        {
            var catalog = await Task.Run(() => _catalogService.GetCatalog());
            var asrModels = catalog.Models.Where(m => string.Equals(m.ModelType, "asr", StringComparison.OrdinalIgnoreCase)).ToList();

            var readyItems = new List<ModelSelectorItem>();
            foreach (var m in asrModels)
            {
                var status = _cacheManager.GetStatus(m);
                if (status.State == ModelCacheState.Ready)
                {
                    string backend = m.Id.StartsWith("llama-cpp") ? "llama.cpp / whisper.cpp GGUF" : "Sherpa-ONNX";
                    readyItems.Add(new ModelSelectorItem(m.Id, m.Name, m.DeviceTierRecommendation, m.QuantVariant, backend, true));
                }
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                AvailableAsrModels.Clear();
                foreach (var item in readyItems)
                {
                    AvailableAsrModels.Add(item);
                }

                string savedAsrId = Preferences.Default.Get("DefaultAsrModelId", string.Empty);
                var matched = AvailableAsrModels.FirstOrDefault(m => string.Equals(m.Id, savedAsrId, StringComparison.OrdinalIgnoreCase));
                SelectedAsrModel = matched ?? AvailableAsrModels.FirstOrDefault();

                OnPropertyChanged(nameof(HasDownloadedModels));
                OnPropertyChanged(nameof(HasNoDownloadedModels));
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load pipeline models in settings: {ex.Message}");
        }
    }

    public async Task LoadProviderSettingsAsync()
    {
        var settings = await _providerSettingsStore.GetSettingsAsync();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BaseUrl = settings.BaseUrl ?? string.Empty;
            ApiKey = settings.ApiKey ?? string.Empty;
            ModelName = settings.ModelName ?? string.Empty;
            TimeoutSeconds = settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 60;
        });
    }

    public async Task SaveProviderSettingsAsync()
    {
        IsSavingProviderSettings = true;
        try
        {
            var settings = new ProviderSettings
            {
                BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl.Trim(),
                ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim(),
                ModelName = string.IsNullOrWhiteSpace(ModelName) ? null : ModelName.Trim(),
                TimeoutSeconds = TimeoutSeconds > 0 ? TimeoutSeconds : 60
            };

            await _providerSettingsStore.SaveSettingsAsync(settings);
            ConnectionStatus = "Provider settings saved successfully.";
            await Shell.Current.DisplayAlertAsync("Saved", "AI Provider configuration saved.", "OK");
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Failed to save: {ex.Message}";
            await Shell.Current.DisplayAlertAsync("Error", $"Could not save settings: {ex.Message}", "OK");
        }
        finally
        {
            IsSavingProviderSettings = false;
        }
    }

    public async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            ConnectionStatus = "Base URL must not be empty.";
            await Shell.Current.DisplayAlertAsync("Validation", "Please enter a Base URL (e.g. http://localhost:11434/v1).", "OK");
            return;
        }

        IsTestingConnection = true;
        ConnectionStatus = "Testing connection to endpoint...";

        try
        {
            var currentSettings = new ProviderSettings
            {
                BaseUrl = BaseUrl.Trim(),
                ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim(),
                ModelName = string.IsNullOrWhiteSpace(ModelName) ? null : ModelName.Trim(),
                TimeoutSeconds = TimeoutSeconds > 0 ? TimeoutSeconds : 15
            };

            var testProvider = new OpenAiCompatibleProvider(currentSettings);
            var result = await testProvider.TestConnectionAsync();

            if (result.Success)
            {
                ConnectionStatus = $"Success: {result.Message}";
                await Shell.Current.DisplayAlertAsync("Connected", $"Connection verified!\n\n{result.Message}", "OK");
            }
            else
            {
                ConnectionStatus = $"Connection failed: {result.Message}";
                await Shell.Current.DisplayAlertAsync("Connection Failed", $"Could not reach endpoint:\n\n{result.Message}", "OK");
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
            await Shell.Current.DisplayAlertAsync("Connection Error", ex.Message, "OK");
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    public async Task LoadVoiceprintsAsync()
    {
        IsLoadingVoiceprints = true;
        try
        {
            var list = await _voiceprintRepo.GetAllAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                EnrolledVoiceprints.Clear();
                foreach (var vp in list)
                {
                    EnrolledVoiceprints.Add(vp);
                }
            });
        }
        finally
        {
            IsLoadingVoiceprints = false;
        }
    }

    private async Task RenameVoiceprintAsync(Voiceprint? vp)
    {
        if (vp == null) return;

        string current = vp.DisplayName;
        string result = await Shell.Current.DisplayPromptAsync(
            "Rename Voiceprint",
            $"Enter new display name for '{current}':",
            initialValue: current);

        if (string.IsNullOrWhiteSpace(result) || result.Trim() == current) return;

        var updated = new Voiceprint(vp.Id, result.Trim(), vp.Embedding, vp.CreatedAt);
        await _voiceprintRepo.SaveAsync(updated);
        await LoadVoiceprintsAsync();
    }

    private async Task DeleteVoiceprintAsync(Voiceprint? vp)
    {
        if (vp == null) return;

        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Voiceprint",
            $"Are you sure you want to permanently delete the enrolled voiceprint for '{vp.DisplayName}'?\n\nThis removes the biometric vector from your device.",
            "Delete", "Cancel");

        if (!confirm) return;

        await _voiceprintRepo.DeleteAsync(vp.Id);
        await LoadVoiceprintsAsync();
    }

    private async Task MergeVoiceprintAsync(Voiceprint? source)
    {
        if (source == null || EnrolledVoiceprints.Count < 2)
        {
            await Shell.Current.DisplayAlertAsync("Merge Profiles", "At least two enrolled profiles are required to merge.", "OK");
            return;
        }

        var otherNames = EnrolledVoiceprints
            .Where(vp => vp.Id != source.Id)
            .Select(vp => vp.DisplayName)
            .ToArray();

        string targetName = await Shell.Current.DisplayActionSheetAsync(
            $"Merge '{source.DisplayName}' into:",
            "Cancel",
            null,
            otherNames);

        if (string.IsNullOrEmpty(targetName) || targetName == "Cancel") return;

        var target = EnrolledVoiceprints.FirstOrDefault(vp => vp.DisplayName == targetName && vp.Id != source.Id);
        if (target == null) return;

        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Confirm Merge",
            $"Merge '{source.DisplayName}' into '{target.DisplayName}'?\n\nTheir embedding vectors will be averaged and '{source.DisplayName}' will be deleted.",
            "Merge", "Cancel");

        if (!confirm) return;

        var srcSpan = source.Embedding.Span;
        var tgtSpan = target.Embedding.Span;
        int len = Math.Min(srcSpan.Length, tgtSpan.Length);
        var merged = new float[len];
        SpeakerEmbeddingMath.AverageAndNormalize(srcSpan.Slice(0, len), tgtSpan.Slice(0, len), merged);

        var updatedTarget = new Voiceprint(target.Id, target.DisplayName, merged, DateTimeOffset.UtcNow);
        await _voiceprintRepo.SaveAsync(updatedTarget);
        await _voiceprintRepo.DeleteAsync(source.Id);

        await LoadVoiceprintsAsync();
        await Shell.Current.DisplayAlertAsync("Merged", $"Profiles successfully merged into '{target.DisplayName}'.", "OK");
    }
}
