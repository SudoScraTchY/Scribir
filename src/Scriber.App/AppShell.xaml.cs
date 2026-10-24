using Scriber.App.Views;

namespace Scriber.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("transcript", typeof(TranscriptPage));
        Routing.RegisterRoute("models", typeof(ModelsPage));
    }
}
