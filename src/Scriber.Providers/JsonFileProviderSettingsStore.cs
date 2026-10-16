namespace Scriber.Providers;

using System.Text.Json;

public sealed class JsonFileProviderSettingsStore : IProviderSettingsStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonFileProviderSettingsStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public async Task<ProviderSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return ProviderSettings.Empty;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var settings = await JsonSerializer.DeserializeAsync<ProviderSettings>(stream, JsonOptions, cancellationToken);
            return settings ?? ProviderSettings.Empty;
        }
        catch
        {
            return ProviderSettings.Empty;
        }
    }

    public async Task SaveSettingsAsync(ProviderSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string? dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
