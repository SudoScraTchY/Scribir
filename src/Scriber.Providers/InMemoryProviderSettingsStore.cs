namespace Scriber.Providers;

public sealed class InMemoryProviderSettingsStore : IProviderSettingsStore
{
    private ProviderSettings _settings;

    public InMemoryProviderSettingsStore(ProviderSettings? initialSettings = null)
    {
        _settings = initialSettings ?? ProviderSettings.Empty;
    }

    public Task<ProviderSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_settings);
    }

    public Task SaveSettingsAsync(ProviderSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        return Task.CompletedTask;
    }
}
