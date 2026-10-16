namespace Scriber.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriber.Core.Domain;
using Scriber.Providers;
using Scriber.Storage;
using Scriber.Storage.Models;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly IModelCacheManager _cacheManager;
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

    public string AppName => "Scribir";
    public string Version => "1.0.0 (Phase 2 - Offline Native)";
    public string CacheDirectory => _cacheManager.CacheDirectory;
    public string DatabasePath { get; }
    public string ArchitectureNotes => "Offline-first, on-device AI transcription & cross-session speaker identity powered by ONNX Runtime and sherpa-onnx.";

    // BYOK Provider Properties (PH2-INT-001)
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

    public SettingsViewModel(
        IModelCacheManager cacheManager,
        IVoiceprintRepository voiceprintRepo,
        IProviderSettingsStore providerSettingsStore,
        ICompletionProvider completionProvider,
        string databasePath)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _voiceprintRepo = voiceprintRepo ?? throw new ArgumentNullException(nameof(voiceprintRepo));
        _providerSettingsStore = providerSettingsStore ?? throw new ArgumentNullException(nameof(providerSettingsStore));
        _completionProvider = completionProvider ?? throw new ArgumentNullException(nameof(completionProvider));
        DatabasePath = databasePath;

        LoadVoiceprintsCommand = new Command(async () => await LoadVoiceprintsAsync());
        RenameVoiceprintCommand = new Command<Voiceprint>(async vp => await RenameVoiceprintAsync(vp));
        DeleteVoiceprintCommand = new Command<Voiceprint>(async vp => await DeleteVoiceprintAsync(vp));
        MergeVoiceprintCommand = new Command<Voiceprint>(async vp => await MergeVoiceprintAsync(vp));
        SaveProviderSettingsCommand = new Command(async () => await SaveProviderSettingsAsync());
        TestConnectionCommand = new Command(async () => await TestConnectionAsync());

        Task.Run(InitializeAsync);
    }

    private async Task InitializeAsync()
    {
        await LoadProviderSettingsAsync();
        await LoadVoiceprintsAsync();
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
            await Shell.Current.DisplayAlert("Saved", "AI Provider configuration saved.", "OK");
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Failed to save: {ex.Message}";
            await Shell.Current.DisplayAlert("Error", $"Could not save settings: {ex.Message}", "OK");
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
            await Shell.Current.DisplayAlert("Validation", "Please enter a Base URL (e.g. http://localhost:11434/v1).", "OK");
            return;
        }

        IsTestingConnection = true;
        ConnectionStatus = "Testing connection to endpoint...";

        try
        {
            // Temporarily build provider with current on-screen settings
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
                await Shell.Current.DisplayAlert("Connected", $"Connection verified!\n\n{result.Message}", "OK");
            }
            else
            {
                ConnectionStatus = $"Connection failed: {result.Message}";
                await Shell.Current.DisplayAlert("Connection Failed", $"Could not reach endpoint:\n\n{result.Message}", "OK");
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
            await Shell.Current.DisplayAlert("Connection Error", ex.Message, "OK");
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

        bool confirm = await Shell.Current.DisplayAlert(
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
            await Shell.Current.DisplayAlert("Merge Profiles", "At least two enrolled profiles are required to merge.", "OK");
            return;
        }

        var otherNames = EnrolledVoiceprints
            .Where(vp => vp.Id != source.Id)
            .Select(vp => vp.DisplayName)
            .ToArray();

        string targetName = await Shell.Current.DisplayActionSheet(
            $"Merge '{source.DisplayName}' into:",
            "Cancel",
            null,
            otherNames);

        if (string.IsNullOrEmpty(targetName) || targetName == "Cancel") return;

        var target = EnrolledVoiceprints.FirstOrDefault(vp => vp.DisplayName == targetName && vp.Id != source.Id);
        if (target == null) return;

        bool confirm = await Shell.Current.DisplayAlert(
            "Confirm Merge",
            $"Merge '{source.DisplayName}' into '{target.DisplayName}'?\n\nTheir embedding vectors will be averaged and '{source.DisplayName}' will be deleted.",
            "Merge", "Cancel");

        if (!confirm) return;

        // Average the embeddings and L2-normalize
        var srcSpan = source.Embedding.Span;
        var tgtSpan = target.Embedding.Span;
        int len = Math.Min(srcSpan.Length, tgtSpan.Length);
        var merged = new float[len];
        float norm = 0f;

        for (int i = 0; i < len; i++)
        {
            merged[i] = (srcSpan[i] + tgtSpan[i]) * 0.5f;
            norm += merged[i] * merged[i];
        }

        norm = MathF.Sqrt(norm);
        if (norm > 0f)
        {
            for (int i = 0; i < len; i++) merged[i] /= norm;
        }

        var updatedTarget = new Voiceprint(target.Id, target.DisplayName, merged, DateTimeOffset.UtcNow);
        await _voiceprintRepo.SaveAsync(updatedTarget);
        await _voiceprintRepo.DeleteAsync(source.Id);

        await LoadVoiceprintsAsync();
        await Shell.Current.DisplayAlert("Merged", $"Profiles successfully merged into '{target.DisplayName}'.", "OK");
    }
}
