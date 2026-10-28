using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Scriber.App.Services;

namespace Scriber.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionSend },
    Categories = new[] { Intent.CategoryDefault },
    DataMimeTypes = new[] { "audio/*", "video/mp4", "video/x-matroska", "application/ogg" })]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeTypes = new[] { "audio/*", "video/mp4", "video/x-matroska", "application/ogg" })]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "scriber")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ProcessIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        ProcessIntent(intent);
    }

    private void ProcessIntent(Intent? intent)
    {
        if (intent == null) return;

        Android.Net.Uri? dataUri = null;

        if (intent.Action == Intent.ActionSend)
        {
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
            {
#pragma warning disable CA1416
                dataUri = (Android.Net.Uri?)intent.GetParcelableExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(Android.Net.Uri)));
#pragma warning restore CA1416
            }
            else
            {
#pragma warning disable CA1422, CS0618
                dataUri = intent.GetParcelableExtra(Intent.ExtraStream) as Android.Net.Uri;
#pragma warning restore CA1422, CS0618
            }
        }
        else if (intent.Action == Intent.ActionView)
        {
            dataUri = intent.Data;
        }

        if (dataUri != null && Uri.TryCreate(dataUri.ToString(), UriKind.Absolute, out var parsedUri))
        {
            Task.Run(async () =>
            {
                await Task.Delay(600); // Allow Shell to mount initial page
                var handler = IPlatformApplication.Current?.Services.GetService<IMediaLaunchHandler>();
                if (handler != null && await handler.CanHandleAsync(parsedUri))
                {
                    await handler.HandleIncomingMediaAsync(parsedUri);
                }
            });
        }
    }
}
