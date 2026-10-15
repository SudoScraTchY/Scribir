using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Versioned JSON DTO models for developer-grade export of complete transcript data.
/// </summary>
public record TranscriptJsonDto
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("language_code")]
    public string LanguageCode { get; init; } = string.Empty;

    [JsonPropertyName("speakers")]
    public List<SpeakerJsonDto> Speakers { get; init; } = [];

    [JsonPropertyName("segments")]
    public List<SegmentJsonDto> Segments { get; init; } = [];

    [JsonPropertyName("audio")]
    public AudioReferenceJsonDto Audio { get; init; } = new();
}

public record SpeakerJsonDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("color_hex")]
    public string ColorHex { get; init; } = string.Empty;
}

public record SegmentJsonDto
{
    [JsonPropertyName("speaker_id")]
    public string SpeakerId { get; init; } = string.Empty;

    [JsonPropertyName("start_seconds")]
    public double StartSeconds { get; init; }

    [JsonPropertyName("end_seconds")]
    public double EndSeconds { get; init; }

    [JsonPropertyName("words")]
    public List<WordJsonDto> Words { get; init; } = [];
}

public record WordJsonDto
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("start_seconds")]
    public double StartSeconds { get; init; }

    [JsonPropertyName("end_seconds")]
    public double EndSeconds { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}

public record AudioReferenceJsonDto
{
    [JsonPropertyName("local_path")]
    public string LocalPath { get; init; } = string.Empty;

    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; init; }

    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; init; }
}

/// <summary>
/// Exports and imports complete, lossless transcript JSON data with schema versioning.
/// Implements PH1-EXPORT-001.
/// </summary>
public sealed class JsonExporter : ITranscriptExporter
{
    public string FormatName => "JSON";
    public string FileExtension => ".json";
    public string MimeType => "application/json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Export(Transcript transcript, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var dto = ToDto(transcript);
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public async Task ExportAsync(Transcript transcript, Stream outputStream, ExportOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(outputStream);

        var dto = ToDto(transcript);
        await JsonSerializer.SerializeAsync(outputStream, dto, JsonOptions, ct).ConfigureAwait(false);
    }

    public static Transcript Import(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var dto = JsonSerializer.Deserialize<TranscriptJsonDto>(json, JsonOptions)
                  ?? throw new JsonException("Failed to deserialize transcript JSON.");

        if (dto.SchemaVersion != "1.0")
            throw new NotSupportedException($"Unsupported schema version: '{dto.SchemaVersion}'. Expected '1.0'.");

        var speakers = dto.Speakers.Select(s => new Speaker(s.Id, s.DisplayName, s.ColorHex)).ToList();

        var segments = dto.Segments.Select(seg =>
        {
            var words = seg.Words.Select(w => new Word(
                w.Text,
                TimeSpan.FromSeconds(w.StartSeconds),
                TimeSpan.FromSeconds(w.EndSeconds),
                w.Confidence)).ToList();

            return new Segment(
                seg.SpeakerId,
                TimeSpan.FromSeconds(seg.StartSeconds),
                TimeSpan.FromSeconds(seg.EndSeconds),
                words);
        }).ToList();

        var audio = new AudioReference(
            dto.Audio.LocalPath,
            TimeSpan.FromSeconds(dto.Audio.DurationSeconds),
            dto.Audio.SampleRate);

        return new Transcript(
            dto.Id,
            dto.Title,
            dto.CreatedAt,
            dto.LanguageCode,
            speakers,
            segments,
            audio);
    }

    public static TranscriptJsonDto ToDto(Transcript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        return new TranscriptJsonDto
        {
            SchemaVersion = "1.0",
            Id = transcript.Id,
            Title = transcript.Title,
            CreatedAt = transcript.CreatedAt,
            LanguageCode = transcript.LanguageCode,
            Speakers = transcript.Speakers.Select(s => new SpeakerJsonDto
            {
                Id = s.Id,
                DisplayName = s.DisplayName,
                ColorHex = s.ColorHex
            }).ToList(),
            Segments = transcript.Segments.Select(seg => new SegmentJsonDto
            {
                SpeakerId = seg.SpeakerId,
                StartSeconds = seg.Start.TotalSeconds,
                EndSeconds = seg.End.TotalSeconds,
                Words = seg.Words.Select(w => new WordJsonDto
                {
                    Text = w.Text,
                    StartSeconds = w.Start.TotalSeconds,
                    EndSeconds = w.End.TotalSeconds,
                    Confidence = w.Confidence
                }).ToList()
            }).ToList(),
            Audio = new AudioReferenceJsonDto
            {
                LocalPath = transcript.Audio.LocalPath,
                DurationSeconds = transcript.Audio.Duration.TotalSeconds,
                SampleRate = transcript.Audio.SampleRate
            }
        };
    }
}
