using Microsoft.Extensions.Logging;
using Scriber.App.Services;
using Scriber.App.ViewModels;
using Scriber.App.Views;
using Scriber.Core.Inference;
using Scriber.Storage;
using Scriber.Storage.Models;

using Scriber.App.Services.Audio;

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
        builder.Services.AddSingleton<IModelCatalogService>(_ => new ModelCatalogService());
        builder.Services.AddSingleton<ISpeakerEmbedder>(sp =>
        {
            var cache = sp.GetRequiredService<IModelCacheManager>();
            string modelFile = Path.Combine(cache.CacheDirectory, "3dspeaker-eres2net-base", "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx");
            if (File.Exists(modelFile))
            {
                try
                {
                    return new Scriber.Inference.SpeakerId.SherpaSpeakerEmbedder(new Scriber.Inference.SpeakerId.SherpaSpeakerEmbedderConfig
                    {
                        ModelPath = modelFile,
                        NumThreads = 2
                    });
                }
                catch { }
            }

            return new Scriber.Inference.SpeakerId.InMemorySpeakerEmbedder();
        });
        builder.Services.AddSingleton<Scriber.Inference.SpeakerId.SpeakerMatcher>();
        builder.Services.AddSingleton<IPlaybackService, PlaybackService>();
        builder.Services.AddSingleton<IPipelineFactory>(sp => new PipelineFactory(
            sp.GetRequiredService<IModelCacheManager>(),
            sp.GetRequiredService<IVoiceprintRepository>(),
            sp.GetRequiredService<ISpeakerEmbedder>(),
            sp.GetRequiredService<Scriber.Inference.SpeakerId.SpeakerMatcher>()));
        builder.Services.AddTransient<IAudioRecorder>(_ => AudioRecorderFactory.Create());

        // BYOK Provider & Summarizer (PH2-INT-001, PH2-INT-003)
        builder.Services.AddSingleton<Scriber.Providers.IProviderSettingsStore>(_ => new Scriber.Providers.JsonFileProviderSettingsStore(providerSettingsPath));
        builder.Services.AddSingleton<Scriber.Providers.ICompletionProvider>(sp => new Scriber.Providers.OpenAiCompatibleProvider(sp.GetRequiredService<Scriber.Providers.IProviderSettingsStore>()));
        builder.Services.AddSingleton<Scriber.Providers.ISummarizer, Scriber.Providers.TranscriptSummarizer>();

        // 2. ViewModels
        builder.Services.AddSingleton<TranscribeViewModel>();
        builder.Services.AddSingleton<LibraryViewModel>();
        builder.Services.AddTransient<TranscriptViewModel>();
        builder.Services.AddSingleton<ModelsViewModel>();
        builder.Services.AddSingleton(sp => new SettingsViewModel(
            sp.GetRequiredService<IModelCacheManager>(),
            sp.GetRequiredService<IModelCatalogService>(),
            sp.GetRequiredService<IVoiceprintRepository>(),
            sp.GetRequiredService<Scriber.Providers.IProviderSettingsStore>(),
            sp.GetRequiredService<Scriber.Providers.ICompletionProvider>(),
            dbPath));

        // 3. Views (Shell Tab roots are Singletons for instant switching without lag)
        builder.Services.AddSingleton<TranscribePage>();
        builder.Services.AddSingleton<LibraryPage>();
        builder.Services.AddTransient<TranscriptPage>();
        builder.Services.AddSingleton<ModelsPage>();
        builder.Services.AddSingleton<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
