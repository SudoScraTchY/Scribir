using System.Text.Json;
using Scriber.Storage.Models;
using Xunit;

namespace Scriber.Storage.Tests;

public class ModelManifestTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    [Fact]
    public void CatalogJson_ValidatesSuccessfullyAgainstRules()
    {
        string catalogPath = Path.Combine(RepoRoot(), "models", "catalog.json");
        Assert.True(File.Exists(catalogPath), $"catalog.json not found at {catalogPath}");

        string json = File.ReadAllText(catalogPath);
        var manifest = JsonSerializer.Deserialize<ModelManifest>(json);

        Assert.NotNull(manifest);
        Assert.Equal("1.0", manifest.SchemaVersion);
        Assert.NotEmpty(manifest.Models);

        // Run full validation
        ModelManifestValidator.Validate(manifest);

        // Verify required initial models
        Assert.Contains(manifest.Models, m => m.Id == "sherpa-whisper-small-int8" && m.ModelType == "asr");
        Assert.Contains(manifest.Models, m => m.Id == "snakers-silero-vad-v5" && m.ModelType == "vad");
        Assert.Contains(manifest.Models, m => m.Id == "nemotron3-diarizer-v3" && m.ModelType == "diarization");
        Assert.Contains(manifest.Models, m => m.Id == "whisper-small-fa-int8" && m.LanguageCodes.Contains("fa"));

        // Verify strict zero-placeholder WER rule
        foreach (var m in manifest.Models)
        {
            Assert.Null(m.MeasuredWer); // Strict: null until verified real benchmarks
        }
    }

    [Fact]
    public void ValidateEntry_RejectsUnauthorizedMirror_PerAdr0009()
    {
        var entry = new ModelManifestEntry
        {
            Id = "fake-model",
            Name = "Fake Model",
            ModelType = "asr",
            LanguageCodes = ["en"],
            DeviceTierRecommendation = "medium",
            SizeBytes = 1000,
            QuantVariant = "int8",
            License = "MIT",
            SourceUrl = "https://unauthorized-mirror-host.com/models/fake.onnx", // Forbidden mirror!
            DateAdded = "2026-10-15",
            Files =
            [
                new()
                {
                    Filename = "fake.onnx",
                    Url = "https://unauthorized-mirror-host.com/models/fake.onnx",
                    Sha256 = new string('a', 64),
                    SizeBytes = 1000
                }
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ModelManifestValidator.ValidateEntry(entry));
        Assert.Contains("ADR-0009", ex.Message);
        Assert.Contains("Zero-Hosting", ex.Message);
    }

    [Fact]
    public void ValidateEntry_RejectsInvalidSha256()
    {
        var entry = new ModelManifestEntry
        {
            Id = "test-model",
            Name = "Test Model",
            ModelType = "asr",
            LanguageCodes = ["en"],
            DeviceTierRecommendation = "low",
            SizeBytes = 500,
            QuantVariant = "int8",
            License = "Apache-2.0",
            SourceUrl = "https://huggingface.co/openai/whisper-tiny",
            DateAdded = "2026-10-15",
            Files =
            [
                new()
                {
                    Filename = "tiny.onnx",
                    Url = "https://huggingface.co/openai/whisper-tiny/resolve/main/tiny.onnx",
                    Sha256 = "invalid_short_hash", // Invalid SHA-256
                    SizeBytes = 500
                }
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ModelManifestValidator.ValidateEntry(entry));
        Assert.Contains("sha256", ex.Message);
    }

    [Fact]
    public void ValidateEntry_RejectsInvalidWer()
    {
        var entry = new ModelManifestEntry
        {
            Id = "test-wer-model",
            Name = "Test WER Model",
            ModelType = "asr",
            LanguageCodes = ["en"],
            DeviceTierRecommendation = "low",
            SizeBytes = 500,
            QuantVariant = "int8",
            License = "Apache-2.0",
            SourceUrl = "https://huggingface.co/openai/whisper-tiny",
            MeasuredWer = 1.5, // Invalid (> 1.0)
            DateAdded = "2026-10-15",
            Files =
            [
                new()
                {
                    Filename = "tiny.onnx",
                    Url = "https://huggingface.co/openai/whisper-tiny/resolve/main/tiny.onnx",
                    Sha256 = new string('0', 64),
                    SizeBytes = 500
                }
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ModelManifestValidator.ValidateEntry(entry));
        Assert.Contains("measured_wer", ex.Message);
    }

    [Fact]
    public void ModelCatalogService_LoadsAndValidatesCatalogSuccessfully()
    {
        var service = new ModelCatalogService();
        var catalog = service.GetCatalog();

        Assert.NotNull(catalog);
        Assert.Equal("1.0", catalog.SchemaVersion);
        Assert.True(catalog.Models.Count >= 4);

        var whisper = catalog.Models.FirstOrDefault(m => m.Id == "sherpa-whisper-small-int8");
        Assert.NotNull(whisper);
        Assert.Equal("Apache-2.0", whisper.License);
        Assert.Equal("medium", whisper.DeviceTierRecommendation);
        Assert.True(whisper.SizeBytes > 0);
        Assert.NotEmpty(whisper.Files);
    }
}
