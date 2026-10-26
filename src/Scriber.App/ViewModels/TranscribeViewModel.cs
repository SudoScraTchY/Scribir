using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Scriber.App.Services;
using Scriber.App.Services.Audio;
using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Scriber.Inference.Diarization;
using Scriber.Pipeline;
using Scriber.Pipeline.Streaming;
using Scriber.Storage;
using Scriber.Storage.Models;

namespace Scriber.App.ViewModels;

public sealed record LanguageItem(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record ModelSelectorItem(string Id, string Name, string Tier, string Quant, string BackendType, bool IsReady)
{
    public string DisplayName => $"{Name} ({BackendType}) ✓";
    public override string ToString() => DisplayName;
}

public record LiveSegmentItem(string SpeakerId, string SpeakerDisplayName, string Text, string Timestamp, string SpeakerColor);

/// <summary>
/// ViewModel for Flow A (File Import) and Flow B (Live Microphone Recording) transcription workflows.
/// Strictly enforces downloaded-only model selection, multi-model pipeline combination (ASR + Diarization + VAD + Voiceprints),
/// live session expansion/minimization, and raw audio session preservation.
/// </summary>
public sealed class TranscribeViewModel : BaseViewModel, IDisposable
{
    public static readonly IReadOnlyList<LanguageItem> SupportedLanguages =
    [
        new("auto", "🌐 Auto (Detect Spoken Language)"),
        new("en", "English"),
        new("fa", "Persian / Farsi (فارسی)"),
        new("es", "Spanish (Español)"),
        new("fr", "French (Français)"),
        new("de", "German (Deutsch)"),
        new("ar", "Arabic (العربية)"),
        new("zh", "Chinese (中文)"),
        new("ja", "Japanese (日本語)"),
        new("ru", "Russian (Русский)"),
        new("tr", "Turkish (Türkçe)"),
        new("it", "Italian (Italiano)"),
        new("pt", "Portuguese (Português)"),
        new("hi", "Hindi (हिन्दी)"),
        new("ko", "Korean (한국어)"),
        new("nl", "Dutch (Nederlands)"),
        new("sv", "Swedish (Svenska)"),
        new("uk", "Ukrainian (Українська)")
    ];

    private readonly IPipelineFactory _pipelineFactory;
    private readonly ITranscriptRepository _repository;
    private readonly IAudioRecorder _audioRecorder;
    private readonly IModelCatalogService? _catalogService;
    private readonly IModelCacheManager? _cacheManager;
    private ILiveTranscriptionSession? _liveSession;

    // Mode: 0 = Live Record, 1 = Import File
    private int _selectedModeIndex = 0;

    // Configuration state
    private LanguageItem _selectedLanguageItem;
    private ModelSelectorItem? _selectedModel;
    private string _selectedSpeakerCount = "Auto";
    private bool _saveRawAudio = true;

    // Multi-Model Pipeline Stack Settings
    private bool _enableDiarization = true;
    private bool _enableVad = true;
    private bool _enableVoiceprints = true;
    private bool _isDiarizerReady = false;
    private bool _isVadReady = false;
    private bool _isVoiceprintReady = false;

    // File selection state
    private string? _selectedFilePath;
    private string _selectedFileName = "No file selected";

    // Live Recording state
    private bool _isRecording;
    private bool _isPaused;
    private TimeSpan _recordingDuration = TimeSpan.Zero;
    private float _audioLevel;
    private string _recordingStatus = "Ready to record";

    // Transcription Pipeline progress
    private bool _isProcessing;
    private double _progressFraction;
    private string _progressStage = "Ready";
    private string _statusMessage = string.Empty;

    public ObservableCollection<ModelSelectorItem> AvailableModels { get; } = [];
    public IReadOnlyList<LanguageItem> AvailableLanguages => SupportedLanguages;
    public IReadOnlyList<string> SpeakerCountOptions { get; } = ["Auto", "1", "2", "3", "4", "5", "6", "7", "8"];
    public ObservableCollection<LiveSegmentItem> LiveDraftSegments { get; } = new();

    public int SelectedModeIndex
    {
        get => _selectedModeIndex;
        set
        {
            if (SetProperty(ref _selectedModeIndex, value))
            {
                OnPropertyChanged(nameof(IsRecordMode));
                OnPropertyChanged(nameof(IsImportMode));
            }
        }
    }

    public bool IsRecordMode => _selectedModeIndex == 0;
    public bool IsImportMode => _selectedModeIndex == 1;

    public bool HasDownloadedModels => AvailableModels.Count > 0;
    public bool HasNoDownloadedModels => AvailableModels.Count == 0;

    public LanguageItem SelectedLanguageItem
    {
        get => _selectedLanguageItem;
        set
        {
            if (SetProperty(ref _selectedLanguageItem, value))
            {
                OnPropertyChanged(nameof(SelectedLanguage));
                OnPropertyChanged(nameof(ActiveSessionSummary));
            }
        }
    }

    public string SelectedLanguage
    {
        get => _selectedLanguageItem?.Code ?? "auto";
        set
        {
            var match = SupportedLanguages.FirstOrDefault(l => string.Equals(l.Code, value, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SelectedLanguageItem = match;
            }
        }
    }

    public ModelSelectorItem? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value))
            {
                OnPropertyChanged(nameof(ActiveSessionSummary));
                RefreshCommands();
            }
        }
    }

    public string SelectedSpeakerCount
    {
        get => _selectedSpeakerCount;
        set
        {
            if (SetProperty(ref _selectedSpeakerCount, value))
            {
                OnPropertyChanged(nameof(ActiveSessionSummary));
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
                OnPropertyChanged(nameof(ActiveSessionSummary));
            }
        }
    }

    public bool EnableVad
    {
        get => _enableVad;
        set => SetProperty(ref _enableVad, value);
    }

    public bool EnableVoiceprints
    {
        get => _enableVoiceprints;
        set => SetProperty(ref _enableVoiceprints, value);
    }

    public bool IsDiarizerReady
    {
        get => _isDiarizerReady;
        private set => SetProperty(ref _isDiarizerReady, value);
    }

    public bool IsVadReady
    {
        get => _isVadReady;
        private set => SetProperty(ref _isVadReady, value);
    }

    public bool IsVoiceprintReady
    {
        get => _isVoiceprintReady;
        private set => SetProperty(ref _isVoiceprintReady, value);
    }

    public bool SaveRawAudio
    {
        get => _saveRawAudio;
        set => SetProperty(ref _saveRawAudio, value);
    }

    public string? SelectedFilePath
    {
        get => _selectedFilePath;
        set => SetProperty(ref _selectedFilePath, value);
    }

    public string SelectedFileName
    {
        get => _selectedFileName;
        set => SetProperty(ref _selectedFileName, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
            {
                OnPropertyChanged(nameof(CanChangeSettings));
                OnPropertyChanged(nameof(IsParametersMinimized));
                OnPropertyChanged(nameof(IsParametersExpanded));
                OnPropertyChanged(nameof(ActiveSessionSummary));
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set => SetProperty(ref _isPaused, value);
    }

    public bool IsParametersMinimized => IsRecording;
    public bool IsParametersExpanded => !IsRecording;
    public bool CanChangeSettings => !IsRecording && !IsProcessing;

    public string ActiveSessionSummary
    {
        get
        {
            string modelName = SelectedModel?.Name ?? "No Model";
            string langName = SelectedLanguageItem?.DisplayName ?? "Auto";
            string diarizationSummary = EnableDiarization ? $"{SelectedSpeakerCount} Speaker(s)" : "Solo";
            string vadSummary = EnableVad ? "VAD Active" : "VAD Off";

            if (IsRecording)
            {
                return $"🔒 Live: {modelName} • {langName} • {diarizationSummary} • {vadSummary}";
            }

            return $"🎙️ {modelName} • 🌐 {langName} • 👥 {diarizationSummary} • 🔇 {vadSummary}";
        }
    }

    public TimeSpan RecordingDuration
    {
        get => _recordingDuration;
        set
        {
            if (SetProperty(ref _recordingDuration, value))
            {
                OnPropertyChanged(nameof(FormattedRecordingDuration));
            }
        }
    }

    public string FormattedRecordingDuration =>
        _recordingDuration.Hours > 0
            ? _recordingDuration.ToString(@"hh\:mm\:ss")
            : _recordingDuration.ToString(@"mm\:ss");

    public float AudioLevel
    {
        get => _audioLevel;
        set => SetProperty(ref _audioLevel, value);
    }

    public string RecordingStatus
    {
        get => _recordingStatus;
        set => SetProperty(ref _recordingStatus, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                OnPropertyChanged(nameof(CanChangeSettings));
            }
        }
    }

    public double ProgressFraction
    {
        get => _progressFraction;
        set => SetProperty(ref _progressFraction, value);
    }

    public string ProgressStage
    {
        get => _progressStage;
        set => SetProperty(ref _progressStage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasLiveSegments => LiveDraftSegments.Count > 0;
    public bool HasLiveDraft => LiveDraftSegments.Count > 0;

    // Commands
    public ICommand SelectRecordModeCommand { get; }
    public ICommand SelectImportModeCommand { get; }
    public ICommand StartRecordingCommand { get; }
    public ICommand PauseResumeRecordingCommand { get; }
    public ICommand StopAndTranscribeCommand { get; }
    public ICommand CancelRecordingCommand { get; }
    public ICommand PickFileCommand { get; }
    public ICommand TranscribeFileCommand { get; }
    public ICommand GoToModelsTabCommand { get; }
    public ICommand GoToSettingsCommand { get; }

    public TranscribeViewModel(
        IPipelineFactory pipelineFactory,
        ITranscriptRepository repository,
        IAudioRecorder audioRecorder,
        IModelCatalogService? catalogService = null,
        IModelCacheManager? cacheManager = null)
    {
        _pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _audioRecorder = audioRecorder ?? throw new ArgumentNullException(nameof(audioRecorder));
        _catalogService = catalogService;
        _cacheManager = cacheManager;

        _selectedLanguageItem = SupportedLanguages[0]; // Auto by default

        _audioRecorder.AudioLevelChanged += OnAudioLevelChanged;
        _audioRecorder.ElapsedTimeChanged += OnElapsedTimeChanged;
        _audioRecorder.StateChanged += OnRecordingStateChanged;
        _audioRecorder.AudioDataAvailable += OnAudioDataAvailable;

        SelectRecordModeCommand = new Command(() => SelectedModeIndex = 0);
        SelectImportModeCommand = new Command(() => SelectedModeIndex = 1);

        StartRecordingCommand = new Command(async () => await StartRecordingAsync(), () => !IsRecording && !IsProcessing && HasDownloadedModels);
        PauseResumeRecordingCommand = new Command(async () => await TogglePauseResumeAsync(), () => IsRecording && !IsProcessing);
        StopAndTranscribeCommand = new Command(async () => await StopAndTranscribeAsync(), () => IsRecording && !IsProcessing);
        CancelRecordingCommand = new Command(async () => await CancelRecordingAsync(), () => IsRecording && !IsProcessing);

        PickFileCommand = new Command(async () => await PickFileAsync(), () => !IsProcessing);
        TranscribeFileCommand = new Command(async () => await TranscribeAsync(SelectedFilePath), () => !IsProcessing && !string.IsNullOrEmpty(SelectedFilePath) && HasDownloadedModels);
        GoToModelsTabCommand = new Command(async () => await Shell.Current.GoToAsync("//models"));
        GoToSettingsCommand = new Command(async () => await Shell.Current.GoToAsync("//settings"));

        LoadAvailableModels();
    }

    public void LoadAvailableModels()
    {
        AvailableModels.Clear();

        if (_catalogService != null && _cacheManager != null)
        {
            try
            {
                var catalog = _catalogService.GetCatalog();

                // 1. Filter ONLY models that have been downloaded and verified
                foreach (var entry in catalog.Models.Where(m => string.Equals(m.ModelType, "asr", StringComparison.OrdinalIgnoreCase)))
                {
                    var status = _cacheManager.GetStatus(entry);
                    if (status.State == ModelCacheState.Ready)
                    {
                        string backend = entry.Id.StartsWith("llama-cpp", StringComparison.OrdinalIgnoreCase) ? "llama.cpp" : "ONNX";
                        var item = new ModelSelectorItem(entry.Id, entry.Name, entry.DeviceTierRecommendation, entry.QuantVariant, backend, true);
                        AvailableModels.Add(item);
                    }
                }

                // 2. Check readiness of companion pipeline models
                var diarizerEntry = catalog.Models.FirstOrDefault(m => string.Equals(m.Id, "nemotron3-diarizer-v3", StringComparison.OrdinalIgnoreCase));
                IsDiarizerReady = diarizerEntry != null && _cacheManager.GetStatus(diarizerEntry).State == ModelCacheState.Ready;

                var vadEntry = catalog.Models.FirstOrDefault(m => string.Equals(m.Id, "snakers-silero-vad-v5", StringComparison.OrdinalIgnoreCase));
                IsVadReady = vadEntry != null && _cacheManager.GetStatus(vadEntry).State == ModelCacheState.Ready;

                var spkEntry = catalog.Models.FirstOrDefault(m => string.Equals(m.Id, "3dspeaker-eres2net-base", StringComparison.OrdinalIgnoreCase));
                IsVoiceprintReady = spkEntry != null && _cacheManager.GetStatus(spkEntry).State == ModelCacheState.Ready;
            }
            catch { }
        }

        // Read engine & pipeline preferences configured in Settings
        string prefModelId = Preferences.Default.Get("DefaultAsrModelId", string.Empty);
        var matchedModel = AvailableModels.FirstOrDefault(m => string.Equals(m.Id, prefModelId, StringComparison.OrdinalIgnoreCase));
        SelectedModel = matchedModel ?? AvailableModels.FirstOrDefault();

        string prefLang = Preferences.Default.Get("DefaultLanguageCode", "auto");
        SelectedLanguage = prefLang;

        EnableVad = Preferences.Default.Get("EnableVad", true);
        EnableDiarization = Preferences.Default.Get("EnableDiarization", true);
        SelectedSpeakerCount = Preferences.Default.Get("DefaultSpeakerCount", "Auto");
        EnableVoiceprints = Preferences.Default.Get("EnableVoiceprints", true);
        SaveRawAudio = Preferences.Default.Get("SaveRawAudio", true);

        OnPropertyChanged(nameof(HasDownloadedModels));
        OnPropertyChanged(nameof(HasNoDownloadedModels));
        OnPropertyChanged(nameof(ActiveSessionSummary));

        if (!HasDownloadedModels)
        {
            StatusMessage = "No speech recognition models downloaded. Please download a model from the Models tab.";
        }
        else
        {
            if (string.IsNullOrEmpty(StatusMessage) || StatusMessage.StartsWith("No speech recognition"))
            {
                StatusMessage = "Speech models ready for offline transcription.";
            }
        }

        RefreshCommands();
    }

    private void OnAudioDataAvailable(object? sender, AudioChunkAvailableEventArgs e)
    {
        if (_liveSession == null || _liveSession.IsPaused || e.Count <= 0) return;

        float[] samples = WavHelper.Pcm16ToFloat(e.Buffer.AsSpan(e.Offset, e.Count));
        _ = _liveSession.PushAudioAsync(samples, e.Timestamp);
    }

    private void OnLiveSegmentUpdated(object? sender, LiveSegmentUpdate update)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            string speakerName = update.SpeakerId == "speaker-0" ? "Speaker 1" : "Speaker 2";
            string color = update.SpeakerId == "speaker-0" ? "#4F46E5" : "#10B981";

            var item = new LiveSegmentItem(
                update.SpeakerId,
                speakerName,
                update.Text,
                $"{update.Start:mm\\:ss} - {update.End:mm\\:ss}",
                color);

            LiveDraftSegments.Add(item);
            OnPropertyChanged(nameof(HasLiveSegments));
            OnPropertyChanged(nameof(HasLiveDraft));
        });
    }

    private void OnAudioLevelChanged(object? sender, float level)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AudioLevel = level;
        });
    }

    private void OnElapsedTimeChanged(object? sender, TimeSpan elapsed)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RecordingDuration = elapsed;
        });
    }

    private void OnRecordingStateChanged(object? sender, RecordingStateChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsRecording = e.IsRecording;
            IsPaused = e.IsPaused;
            RecordingDuration = e.ElapsedTime;
            RecordingStatus = e.IsPaused ? "Recording paused" : (e.IsRecording ? "Recording in progress..." : "Ready to record");

            RefreshCommands();
        });
    }

    private async Task StartRecordingAsync()
    {
        try
        {
            if (!HasDownloadedModels || SelectedModel == null)
            {
                StatusMessage = "Please download a speech recognition model from the Models tab first.";
                return;
            }

            bool hasPermission = await _audioRecorder.RequestPermissionAsync();
            if (!hasPermission)
            {
                StatusMessage = "Microphone permission is required to record audio.";
                return;
            }

            RecordingDuration = TimeSpan.Zero;
            AudioLevel = 0f;
            RecordingStatus = "Recording in progress...";

            LiveDraftSegments.Clear();
            OnPropertyChanged(nameof(HasLiveSegments));
            OnPropertyChanged(nameof(HasLiveDraft));

            var diarizationOptions = new DiarizationOptions();
            if (int.TryParse(SelectedSpeakerCount, out int pinCount) && pinCount > 0)
            {
                diarizationOptions = diarizationOptions with { NumSpeakers = pinCount };
            }

            var options = new TranscriptionPipelineOptions
            {
                LanguageCode = SelectedLanguage,
                ModelId = SelectedModel.Id,
                SaveRawAudio = SaveRawAudio,
                EnableDiarization = EnableDiarization,
                EnableVad = EnableVad,
                EnableVoiceprints = EnableVoiceprints,
                DiarizationOptions = diarizationOptions
            };

            _liveSession = _pipelineFactory.CreateLiveSession(options);
            _liveSession.SegmentUpdated += OnLiveSegmentUpdated;

            await _audioRecorder.StartRecordingAsync();
            IsRecording = true;
            IsPaused = false;
            RefreshCommands();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Recording error: {ex.Message}";
            RecordingStatus = "Failed to start";
        }
    }

    private async Task TogglePauseResumeAsync()
    {
        try
        {
            if (IsPaused)
            {
                _liveSession?.Resume();
                await _audioRecorder.ResumeRecordingAsync();
            }
            else
            {
                _liveSession?.Pause();
                await _audioRecorder.PauseRecordingAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pause/Resume error: {ex.Message}";
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        try
        {
            string recordedPath = await _audioRecorder.StopRecordingAsync();
            IsRecording = false;
            IsPaused = false;
            AudioLevel = 0f;
            RefreshCommands();

            Transcript? liveDraft = null;
            if (_liveSession != null)
            {
                liveDraft = await _liveSession.CompleteDraftAsync(recordedPath);
                await _liveSession.DisposeAsync();
                _liveSession = null;
            }

            if (!string.IsNullOrEmpty(recordedPath) && File.Exists(recordedPath))
            {
                string targetAudioPath = recordedPath;

                // Feature: Preserve raw audio session for re-transcription across models and voiceprint sourcing
                if (SaveRawAudio)
                {
                    try
                    {
                        string sessionsDir = Path.Combine(FileSystem.AppDataDirectory, "audio_sessions");
                        Directory.CreateDirectory(sessionsDir);
                        string persistentFile = Path.Combine(sessionsDir, $"session_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.wav");
                        File.Copy(recordedPath, persistentFile, overwrite: true);
                        targetAudioPath = persistentFile;
                    }
                    catch { }
                }

                await TranscribeAsync(targetAudioPath, isLiveRecording: true, liveDraft: liveDraft);
            }
            else
            {
                StatusMessage = "Recording produced no audio file.";
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (Shell.Current != null)
                    {
                        await Shell.Current.DisplayAlertAsync("Recording Notice", "No audio file was produced. Please check your microphone permissions and try speaking for a few seconds.", "OK");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stop recording error: {ex.Message}";
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlertAsync("Recording Error", ex.Message, "OK");
                }
            });
        }
    }

    private async Task CancelRecordingAsync()
    {
        try
        {
            if (_liveSession != null)
            {
                await _liveSession.DisposeAsync();
                _liveSession = null;
            }

            await _audioRecorder.CancelRecordingAsync();
            IsRecording = false;
            IsPaused = false;
            AudioLevel = 0f;
            RecordingDuration = TimeSpan.Zero;
            RecordingStatus = "Recording cancelled";
            LiveDraftSegments.Clear();
            OnPropertyChanged(nameof(HasLiveSegments));
            OnPropertyChanged(nameof(HasLiveDraft));
            RefreshCommands();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cancel recording error: {ex.Message}";
        }
    }

    private async Task PickFileAsync()
    {
        try
        {
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [".mp3", ".wav", ".m4a", ".mp4", ".ogg", ".flac"] },
                { DevicePlatform.Android, ["audio/*", "video/mp4"] }
            });

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select Audio or Video Recording",
                FileTypes = customFileType
            });

            if (result != null)
            {
                SelectedFilePath = result.FullPath;
                SelectedFileName = result.FileName;
                StatusMessage = $"Selected: {result.FileName}";
                ((Command)TranscribeFileCommand).ChangeCanExecute();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"File pick failed: {ex.Message}";
        }
    }

    private async Task TranscribeAsync(string? audioFilePath, bool isLiveRecording = false, Transcript? liveDraft = null)
    {
        if (string.IsNullOrEmpty(audioFilePath)) return;

        if (!HasDownloadedModels || SelectedModel == null)
        {
            StatusMessage = "Please download a speech model from the Models tab first.";
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlertAsync("No Model Ready", "Please download a speech recognition model from the Models tab before transcribing.", "OK");
                }
            });
            return;
        }

        IsProcessing = true;
        RefreshCommands();
        ProgressFraction = 0.0;
        ProgressStage = isLiveRecording ? "Pass 2: Offline Finalization" : "Starting...";
        StatusMessage = isLiveRecording ? "Running high-accuracy offline pass..." : "Initializing transcription pipeline...";

        var progress = new Progress<PipelineProgress>(report =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressStage = report.Stage.ToString();
                ProgressFraction = report.ProgressFraction;
                StatusMessage = report.Message;
            });
        });

        try
        {
            var diarizationOptions = new DiarizationOptions();
            if (int.TryParse(SelectedSpeakerCount, out int pinCount) && pinCount > 0)
            {
                diarizationOptions = diarizationOptions with { NumSpeakers = pinCount };
            }

            string sessionTitle = isLiveRecording
                ? $"Live Recording ({DateTime.Now:MMM dd, HH:mm})"
                : Path.GetFileNameWithoutExtension(audioFilePath);

            var options = new TranscriptionPipelineOptions
            {
                Title = sessionTitle,
                LanguageCode = SelectedLanguage,
                ModelId = SelectedModel.Id,
                SaveRawAudio = SaveRawAudio,
                EnableDiarization = EnableDiarization,
                EnableVad = EnableVad,
                EnableVoiceprints = EnableVoiceprints,
                DiarizationOptions = diarizationOptions
            };

            var pipeline = _pipelineFactory.CreatePipeline(options);
            var transcript = await pipeline.ProcessAsync(audioFilePath, options, progress);

            // Flow B: Reconcile offline high-accuracy transcript with live streaming draft
            if (liveDraft != null)
            {
                var reconciler = _pipelineFactory.CreateReconciler();
                transcript = reconciler.Reconcile(liveDraft, transcript);
            }

            // Persist to SQLite library
            await _repository.SaveAsync(transcript);

            StatusMessage = "Transcript generated and saved successfully!";

            // Ensure navigation is executed on the main UI thread
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    if (Shell.Current != null)
                    {
                        await Shell.Current.GoToAsync($"transcript?id={transcript.Id}");
                    }
                }
                catch (Exception navEx)
                {
                    Debug.WriteLine($"Navigation failed: {navEx.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Transcription error: {ex.Message}";
            ProgressStage = "Failed";

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlertAsync("Transcription Failed", ex.Message, "OK");
                }
            });
        }
        finally
        {
            IsProcessing = false;
            RefreshCommands();
        }
    }

    private void RefreshCommands()
    {
        ((Command)StartRecordingCommand).ChangeCanExecute();
        ((Command)PauseResumeRecordingCommand).ChangeCanExecute();
        ((Command)StopAndTranscribeCommand).ChangeCanExecute();
        ((Command)CancelRecordingCommand).ChangeCanExecute();
        ((Command)PickFileCommand).ChangeCanExecute();
        ((Command)TranscribeFileCommand).ChangeCanExecute();
    }

    public void Dispose()
    {
        _audioRecorder.AudioLevelChanged -= OnAudioLevelChanged;
        _audioRecorder.ElapsedTimeChanged -= OnElapsedTimeChanged;
        _audioRecorder.StateChanged -= OnRecordingStateChanged;
        _audioRecorder.AudioDataAvailable -= OnAudioDataAvailable;

        if (_liveSession != null)
        {
            _liveSession.DisposeAsync().AsTask().ConfigureAwait(false);
            _liveSession = null;
        }
    }
}
