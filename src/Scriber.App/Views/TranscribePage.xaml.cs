using Scriber.App.Controls;
using Scriber.App.ViewModels;

namespace Scriber.App.Views;

public partial class TranscribePage : ContentPage
{
    private readonly TranscribeViewModel _viewModel;
    private readonly AudioWaveformDrawable _waveformDrawable = new();

    public TranscribePage(TranscribeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;

        WaveformCanvas.Drawable = _waveformDrawable;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.AudioLevel))
        {
            _waveformDrawable.PushAudioLevel(_viewModel.AudioLevel);
            MainThread.BeginInvokeOnMainThread(() => WaveformCanvas.Invalidate());
        }
        else if (e.PropertyName == nameof(_viewModel.IsRecording) || e.PropertyName == nameof(_viewModel.IsPaused))
        {
            _waveformDrawable.IsRecording = _viewModel.IsRecording;
            _waveformDrawable.IsPaused = _viewModel.IsPaused;
            if (!_viewModel.IsRecording)
            {
                _waveformDrawable.Reset();
            }
            MainThread.BeginInvokeOnMainThread(() => WaveformCanvas.Invalidate());
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Refresh downloaded models and pipeline readiness on view display
        _viewModel.LoadAvailableModels();

        _waveformDrawable.IsRecording = _viewModel.IsRecording;
        _waveformDrawable.IsPaused = _viewModel.IsPaused;
        WaveformCanvas.Invalidate();
    }
}
