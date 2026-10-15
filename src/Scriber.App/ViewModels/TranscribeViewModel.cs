using System.Windows.Input;
using Scriber.App.Services;
using Scriber.Inference.Diarization;
using Scriber.Pipeline;
using Scriber.Storage;

namespace Scriber.App.ViewModels;

/// <summary>
/// ViewModel for Flow A: File Import & Transcribe screen (PH1-UI-002).
/// </summary>
public sealed class TranscribeViewModel : BaseViewModel
{
    private readonly IPipelineFactory _pipelineFactory;
    private readonly ITranscriptRepository _repository;

    private string? _selectedFilePath;
    private string _selectedFileName = "No file selected";
    private string _selectedLanguage = "en";
    private string _selectedSpeakerCount = "Auto";
    private bool _isProcessing;
    private double _progressFraction;
    private string _progressStage = "Ready";
    private string _statusMessage = string.Empty;

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

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetProperty(ref _selectedLanguage, value);
    }

    public string SelectedSpeakerCount
    {
        get => _selectedSpeakerCount;
        set => SetProperty(ref _selectedSpeakerCount, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
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

    public IReadOnlyList<string> AvailableLanguages { get; } =
        ["en", "fa", "es", "fr", "de", "ar", "zh", "ja", "ru", "tr"];

    public IReadOnlyList<string> SpeakerCountOptions { get; } =
        ["Auto", "1", "2", "3", "4", "5", "6", "7", "8"];

    public ICommand PickFileCommand { get; }
    public ICommand TranscribeCommand { get; }

    public TranscribeViewModel(IPipelineFactory pipelineFactory, ITranscriptRepository repository)
    {
        _pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        PickFileCommand = new Command(async () => await PickFileAsync());
        TranscribeCommand = new Command(async () => await TranscribeAsync(), () => !IsProcessing && !string.IsNullOrEmpty(SelectedFilePath));
    }

    private async Task PickFileAsync()
    {
        try
        {
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [".mp3", ".wav", ".m4a", ".mp4"] },
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
                ((Command)TranscribeCommand).ChangeCanExecute();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"File pick failed: {ex.Message}";
        }
    }

    private async Task TranscribeAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath)) return;

        IsProcessing = true;
        ((Command)TranscribeCommand).ChangeCanExecute();
        ProgressFraction = 0.0;
        ProgressStage = "Starting...";
        StatusMessage = "Initializing transcription pipeline...";

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

            var options = new TranscriptionPipelineOptions
            {
                Title = Path.GetFileNameWithoutExtension(SelectedFilePath),
                LanguageCode = SelectedLanguage,
                DiarizationOptions = diarizationOptions
            };

            var pipeline = _pipelineFactory.CreatePipeline();
            var transcript = await pipeline.ProcessAsync(SelectedFilePath, options, progress);

            // Persist to SQLite library
            await _repository.SaveAsync(transcript);

            StatusMessage = "Transcript generated and saved successfully!";

            // Navigate to transcript view
            await Shell.Current.GoToAsync($"transcript?id={transcript.Id}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            ProgressStage = "Failed";
        }
        finally
        {
            IsProcessing = false;
            ((Command)TranscribeCommand).ChangeCanExecute();
        }
    }
}
