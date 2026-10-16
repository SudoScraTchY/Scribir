namespace Scriber.Providers;

public interface IProviderSettingsStore
{
    Task<ProviderSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(ProviderSettings settings, CancellationToken cancellationToken = default);
}
