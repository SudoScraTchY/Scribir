using System.Text.RegularExpressions;

namespace Scriber.Storage.Models;

/// <summary>
/// Validates ModelManifest and entries against ADR-0009 constraints and manifest.schema.json rules.
/// Implements PH1-MODEL-001.
/// </summary>
public static class ModelManifestValidator
{
    private static readonly Regex IdRegex = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex Sha256Regex = new(@"^[a-fA-F0-9]{64}$", RegexOptions.Compiled);
    private static readonly HashSet<string> ValidModelTypes = new(StringComparer.OrdinalIgnoreCase) { "asr", "diarization", "vad" };
    private static readonly HashSet<string> ValidDeviceTiers = new(StringComparer.OrdinalIgnoreCase) { "low", "medium", "high" };

    public static void Validate(ModelManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.SchemaVersion != "1.0")
            throw new InvalidOperationException($"Invalid schema_version: '{manifest.SchemaVersion}'. Expected '1.0'.");

        if (manifest.Models == null || manifest.Models.Count == 0)
            throw new InvalidOperationException("Manifest must contain at least one model entry.");

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in manifest.Models)
        {
            ValidateEntry(model);

            if (!seenIds.Add(model.Id))
                throw new InvalidOperationException($"Duplicate model id in manifest: '{model.Id}'.");
        }
    }

    public static void ValidateEntry(ModelManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.Id) || !IdRegex.IsMatch(entry.Id))
            throw new InvalidOperationException($"Model id '{entry.Id}' is invalid. Must match '^[a-z0-9]+(-[a-z0-9]+)*$'.");

        if (string.IsNullOrWhiteSpace(entry.Name))
            throw new InvalidOperationException($"Model '{entry.Id}' must have a non-empty name.");

        if (string.IsNullOrWhiteSpace(entry.ModelType) || !ValidModelTypes.Contains(entry.ModelType))
            throw new InvalidOperationException($"Model '{entry.Id}' has invalid model_type '{entry.ModelType}'. Allowed: asr, diarization, vad.");

        if (entry.LanguageCodes == null || entry.LanguageCodes.Count == 0 || entry.LanguageCodes.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"Model '{entry.Id}' must declare at least one non-empty language code.");

        if (string.IsNullOrWhiteSpace(entry.DeviceTierRecommendation) || !ValidDeviceTiers.Contains(entry.DeviceTierRecommendation))
            throw new InvalidOperationException($"Model '{entry.Id}' has invalid device_tier_recommendation '{entry.DeviceTierRecommendation}'. Allowed: low, medium, high.");

        if (entry.SizeBytes <= 0)
            throw new InvalidOperationException($"Model '{entry.Id}' size_bytes must be strictly positive.");

        if (string.IsNullOrWhiteSpace(entry.QuantVariant))
            throw new InvalidOperationException($"Model '{entry.Id}' must declare quant_variant.");

        if (string.IsNullOrWhiteSpace(entry.License))
            throw new InvalidOperationException($"Model '{entry.Id}' must declare license.");

        // Strict ADR-0009 Zero-Hosting Check
        ValidateSourceUrl(entry.Id, entry.SourceUrl);

        if (entry.MeasuredWer.HasValue && (entry.MeasuredWer.Value < 0.0 || entry.MeasuredWer.Value > 1.0))
            throw new InvalidOperationException($"Model '{entry.Id}' measured_wer must be between 0.0 and 1.0, or null.");

        if (string.IsNullOrWhiteSpace(entry.DateAdded))
            throw new InvalidOperationException($"Model '{entry.Id}' must declare date_added.");

        if (entry.Files == null || entry.Files.Count == 0)
            throw new InvalidOperationException($"Model '{entry.Id}' must specify at least one download file.");

        foreach (var file in entry.Files)
        {
            ValidateFile(entry.Id, file);
        }
    }

    private static void ValidateSourceUrl(string modelId, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException($"Model '{modelId}' source_url cannot be empty.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"Model '{modelId}' source_url must be a valid HTTPS URI.");
        }

        // Strict whitelist: Hugging Face or GitHub official releases
        bool isAllowed = uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase) ||
                         uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

        if (!isAllowed)
        {
            throw new InvalidOperationException(
                $"Model '{modelId}' points source_url to unauthorized host '{uri.Host}'. " +
                "Per ADR-0009 (Zero-Hosting), models must point directly to original upstream repositories (Hugging Face or GitHub).");
        }
    }

    private static void ValidateFile(string modelId, ModelFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (string.IsNullOrWhiteSpace(file.Filename))
            throw new InvalidOperationException($"Model '{modelId}' contains a file with an empty filename.");

        if (string.IsNullOrWhiteSpace(file.Url) || !Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Model '{modelId}' file '{file.Filename}' has an invalid download URL.");

        bool isAllowedHost = uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase) ||
                             uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

        if (!isAllowedHost)
        {
            throw new InvalidOperationException(
                $"Model '{modelId}' file '{file.Filename}' download URL points to unauthorized host '{uri.Host}'. Per ADR-0009, mirrors are forbidden.");
        }

        if (string.IsNullOrWhiteSpace(file.Sha256) || !Sha256Regex.IsMatch(file.Sha256))
        {
            throw new InvalidOperationException(
                $"Model '{modelId}' file '{file.Filename}' has invalid sha256 checksum '{file.Sha256}'. Must be 64-char hex.");
        }
    }
}
