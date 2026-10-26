using Scriber.App.ViewModels;

namespace Scriber.App.Views;

public partial class ModelsPage : ContentPage
{
    private readonly ModelsViewModel _viewModel;
    private bool _hasAppearedOnce;

    public ModelsPage(ModelsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Skip blocking on initial appearance since ViewModel initializes catalog asynchronously.
        // On subsequent appearances, refresh statuses smoothly in background.
        if (_hasAppearedOnce)
        {
            _viewModel.RefreshAllStatuses();
        }
        _hasAppearedOnce = true;
    }
}
