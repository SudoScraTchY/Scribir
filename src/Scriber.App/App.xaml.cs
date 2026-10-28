using Microsoft.Extensions.DependencyInjection;
using Scriber.App.Services;

namespace Scriber.App;

public partial class App : Application
{
	private readonly IServiceProvider? _serviceProvider;

	public App(IServiceProvider serviceProvider)
	{
		_serviceProvider = serviceProvider;
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		CheckCommandLineLaunch();
		return window;
	}

	protected override void OnAppLinkRequestReceived(Uri uri)
	{
		base.OnAppLinkRequestReceived(uri);
		var handler = _serviceProvider?.GetService<IMediaLaunchHandler>();
		if (handler != null)
		{
			_ = handler.HandleIncomingMediaAsync(uri);
		}
	}

	private void CheckCommandLineLaunch()
	{
		try
		{
			var args = Environment.GetCommandLineArgs();
			if (args != null && args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
			{
				string raw = args[1].Trim('"', '\'');
				if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
					(File.Exists(raw) && Uri.TryCreate("file:///" + raw.Replace('\\', '/'), UriKind.Absolute, out uri)))
				{
					Task.Run(async () =>
					{
						await Task.Delay(600); // Allow Shell to mount initial page
						var handler = _serviceProvider?.GetService<IMediaLaunchHandler>();
						if (handler != null && await handler.CanHandleAsync(uri))
						{
							await handler.HandleIncomingMediaAsync(uri);
						}
					});
				}
			}
		}
		catch { }
	}
}