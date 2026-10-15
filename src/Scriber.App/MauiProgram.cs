using Microsoft.Extensions.Logging;
using Scriber.App.Services;
using Scriber.App.ViewModels;
using Scriber.App.Views;
using Scriber.Storage;
using Scriber.Storage.Models;

namespace Scriber.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // 1. Storage & Model Manager Services
        string dbPath = Path.Combine(FileSystem.AppDataDirectory, "scriber.db");
        string modelsDir = Path.Combine(FileSystem.AppDataDirectory, "models");

        builder.Services.AddSingleton<ITranscriptRepository>(_ => new SqliteTranscriptRepository(dbPath));
        builder.Services.AddSingleton<IModelCacheManager>(_ => new LocalModelCacheManager(modelsDir));
        builder.Services.AddSingleton<IPlaybackService, PlaybackService>();
        builder.Services.AddSingleton<IPipelineFactory, PipelineFactory>();

        // 2. ViewModels
        builder.Services.AddTransient<TranscribeViewModel>();
        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<TranscriptViewModel>();
        builder.Services.AddTransient(sp => new SettingsViewModel(sp.GetRequiredService<IModelCacheManager>(), dbPath));

        // 3. Views
        builder.Services.AddTransient<TranscribePage>();
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<TranscriptPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
