using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Exports transcript as structured Markdown with YAML frontmatter, designed for PKM tools (Obsidian, Logseq)
/// and agent queries per ADR-0008. Implements PH1-EXPORT-002.
/// </summary>
public sealed class MarkdownExporter : ITranscriptExporter
{
    public string FormatName => "Markdown";
    public string FileExtension => ".md";
    public string MimeType => "text/markdown";

    public string Export(Transcript transcript, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        options ??= new ExportOptions();

        var speakerMap = transcript.Speakers.ToDictionary(s => s.Id, s => s);
        var sb = new StringBuilder();

        // 1. YAML Frontmatter
        sb.AppendLine("---");
        sb.AppendLine($"title: {EscapeYamlString(transcript.Title)}");
        sb.AppendLine($"date: {transcript.CreatedAt:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine($"duration: \"{TimeFormatUtils.FormatHuman(transcript.Audio.Duration)}\"");
        sb.AppendLine($"language: \"{transcript.LanguageCode}\"");
        sb.AppendLine("participants:");
        foreach (var speaker in transcript.Speakers)
        {
            sb.AppendLine($"  - {EscapeYamlString(speaker.DisplayName)}");
        }
        sb.AppendLine("---");
        sb.AppendLine();

        // 2. Body
        foreach (var segment in transcript.Segments)
        {
            speakerMap.TryGetValue(segment.SpeakerId, out var speaker);
            string speakerName = speaker?.DisplayName ?? segment.SpeakerId;

            string startStr = TimeFormatUtils.FormatHuman(segment.Start);
            string endStr = TimeFormatUtils.FormatHuman(segment.End);

            if (options.IncludeTimestamps)
            {
                sb.AppendLine($"### {speakerName} ({startStr} - {endStr})");
            }
            else
            {
                sb.AppendLine($"### {speakerName}");
            }

            var text = string.Join(" ", segment.Words.Select(w => w.Text)).Trim();
            sb.AppendLine(text);

            if (options.IncludeMarkdownWordMetadata && segment.Words.Count > 0)
            {
                var wordDtos = segment.Words.Select(w => new WordJsonDto
                {
                    Text = w.Text,
                    StartSeconds = w.Start.TotalSeconds,
                    EndSeconds = w.End.TotalSeconds,
                    Confidence = w.Confidence
                });
                string jsonWords = JsonSerializer.Serialize(wordDtos, JsonExporter.JsonOptions);
                sb.AppendLine($"<!-- scriber:words:{segment.SpeakerId}:{segment.Start.TotalSeconds:F3}:{segment.End.TotalSeconds:F3} {jsonWords} -->");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task ExportAsync(Transcript transcript, Stream outputStream, ExportOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(outputStream);

        string content = Export(transcript, options);
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        await outputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    private static string EscapeYamlString(string value)
    {
        if (value.Contains('"') || value.Contains(':') || value.Contains('#'))
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }
        return $"\"{value}\"";
    }
}
