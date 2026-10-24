using Android.App;
using Android.Runtime;

namespace Scriber.App;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
		SQLitePCL.Batteries_V2.Init();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
