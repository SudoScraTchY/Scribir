using Scriber.App.ViewModels;

namespace Scriber.App.Views;

public partial class TranscribePage : ContentPage
{
    public TranscribePage(TranscribeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
