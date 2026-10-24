using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Scriber.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();
		this.UnhandledException += (sender, e) =>
		{
			try
			{
				string crashPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "crash.log");
				System.IO.File.WriteAllText(crashPath, $"Message: {e.Message}\nException: {e.Exception?.ToString()}\nStackTrace: {e.Exception?.StackTrace}");
			}
			catch { }
		};
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

