using Scriber.App.ViewModels;

namespace Scriber.App.Views;

public partial class ModelsPage : ContentPage
{
    private readonly ModelsViewModel _viewModel;

    public ModelsPage(ModelsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshAllStatuses();
    }
}
