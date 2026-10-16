using Microsoft.Extensions.Logging;
using Scriber.App.Services;
using Scriber.App.ViewModels;
using Scriber.App.Views;
using Scriber.Core.Inference;
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
        string providerSettingsPath = Path.Combine(FileSystem.AppDataDirectory, "provider_settings.json");

        builder.Services.AddSingleton<ITranscriptRepository>(_ => new SqliteTranscriptRepository(dbPath));
        builder.Services.AddSingleton<IVoiceprintRepository>(_ => new SqliteVoiceprintRepository(dbPath));
        builder.Services.AddSingleton<IModelCacheManager>(_ => new LocalModelCacheManager(modelsDir));
        builder.Services.AddSingleton<ISpeakerEmbedder>(_ => new Scriber.Inference.SpeakerId.InMemorySpeakerEmbedder());
        builder.Services.AddSingleton<Scriber.Inference.SpeakerId.SpeakerMatcher>();
        builder.Services.AddSingleton<IPlaybackService, PlaybackService>();
        builder.Services.AddSingleton<IPipelineFactory, PipelineFactory>();

        // BYOK Provider & Summarizer (PH2-INT-001, PH2-INT-003)
        builder.Services.AddSingleton<Scriber.Providers.IProviderSettingsStore>(_ => new Scriber.Providers.JsonFileProviderSettingsStore(providerSettingsPath));
        builder.Services.AddSingleton<Scriber.Providers.ICompletionProvider>(sp => new Scriber.Providers.OpenAiCompatibleProvider(sp.GetRequiredService<Scriber.Providers.IProviderSettingsStore>()));
        builder.Services.AddSingleton<Scriber.Providers.ISummarizer, Scriber.Providers.TranscriptSummarizer>();

        // 2. ViewModels
        builder.Services.AddTransient<TranscribeViewModel>();
        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<TranscriptViewModel>();
        builder.Services.AddTransient(sp => new SettingsViewModel(
            sp.GetRequiredService<IModelCacheManager>(),
            sp.GetRequiredService<IVoiceprintRepository>(),
            sp.GetRequiredService<Scriber.Providers.IProviderSettingsStore>(),
            sp.GetRequiredService<Scriber.Providers.ICompletionProvider>(),
            dbPath));

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
