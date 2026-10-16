namespace Scriber.Providers;

public sealed record ProviderSettings
{
    public string? BaseUrl { get; init; }
    public string? ApiKey { get; init; }
    public string? ModelName { get; init; }
    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// True if an endpoint URL has been configured.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

    public static ProviderSettings Empty => new();
}
