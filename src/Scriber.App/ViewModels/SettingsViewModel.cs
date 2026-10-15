using Scriber.Storage;
using Scriber.Storage.Models;

namespace Scriber.App.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly IModelCacheManager _cacheManager;

    public string AppName => "Scribir";
    public string Version => "1.0.0 (Phase 1)";
    public string CacheDirectory => _cacheManager.CacheDirectory;
    public string DatabasePath { get; }
    public string ArchitectureNotes => "Offline-first, on-device AI transcription powered by ONNX Runtime and sherpa-onnx.";

    public SettingsViewModel(IModelCacheManager cacheManager, string databasePath)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        DatabasePath = databasePath;
    }
}
