using System.Text.Json.Serialization;

namespace Scriber.Storage.Models;

public record ModelManifest
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("catalog_updated_at")]
    public DateTimeOffset CatalogUpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("models")]
    public List<ModelManifestEntry> Models { get; init; } = [];
}

public record ModelManifestEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("model_type")]
    public string ModelType { get; init; } = string.Empty;

    [JsonPropertyName("language_codes")]
    public List<string> LanguageCodes { get; init; } = [];

    [JsonPropertyName("device_tier_recommendation")]
    public string DeviceTierRecommendation { get; init; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("quant_variant")]
    public string QuantVariant { get; init; } = string.Empty;

    [JsonPropertyName("license")]
    public string License { get; init; } = string.Empty;

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; init; } = string.Empty;

    [JsonPropertyName("measured_wer")]
    public double? MeasuredWer { get; init; }

    [JsonPropertyName("benchmark_notes")]
    public string? BenchmarkNotes { get; init; }

    [JsonPropertyName("date_added")]
    public string DateAdded { get; init; } = string.Empty;

    [JsonPropertyName("files")]
    public List<ModelFileEntry> Files { get; init; } = [];
}

public record ModelFileEntry
{
    [JsonPropertyName("filename")]
    public string Filename { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }
}
