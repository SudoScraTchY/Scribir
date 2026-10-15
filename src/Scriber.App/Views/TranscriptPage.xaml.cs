using Scriber.App.ViewModels;

namespace Scriber.App.Views;

public partial class TranscriptPage : ContentPage
{
    private readonly TranscriptViewModel _viewModel;

    public TranscriptPage(TranscriptViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Dispose();
    }
}
