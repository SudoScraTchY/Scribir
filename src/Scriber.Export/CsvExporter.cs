using System.Text;
using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Exports transcript as RFC 4180 compliant CSV format suitable for Excel, Google Sheets,
/// and data analytics pipelines.
/// </summary>
public sealed class CsvExporter : ITranscriptExporter
{
    public string FormatName => "CSV Spreadsheet";
    public string FileExtension => ".csv";
    public string MimeType => "text/csv";

    public string Export(Transcript transcript, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        options ??= new ExportOptions();

        var speakerMap = transcript.Speakers.ToDictionary(s => s.Id, s => s);
        var sb = new StringBuilder();

        // CSV Header
        sb.AppendLine("Index,SpeakerId,SpeakerName,StartSeconds,EndSeconds,StartTime,EndTime,WordCount,AvgConfidence,Text");

        for (int i = 0; i < transcript.Segments.Count; i++)
        {
            var segment = transcript.Segments[i];
            speakerMap.TryGetValue(segment.SpeakerId, out var speaker);
            string speakerName = speaker?.DisplayName ?? segment.SpeakerId;

            double startSec = segment.Start.TotalSeconds;
            double endSec = segment.End.TotalSeconds;
            string startStr = TimeFormatUtils.FormatHuman(segment.Start);
            string endStr = TimeFormatUtils.FormatHuman(segment.End);

            double avgConfidence = segment.Words.Count > 0
                ? segment.Words.Average(w => w.Confidence)
                : 1.0;

            string fullText = string.Join(" ", segment.Words.Select(w => w.Text)).Trim();

            sb.Append(i + 1).Append(',');
            sb.Append(EscapeCsv(segment.SpeakerId)).Append(',');
            sb.Append(EscapeCsv(speakerName)).Append(',');
            sb.Append(startSec.ToString("F2")).Append(',');
            sb.Append(endSec.ToString("F2")).Append(',');
            sb.Append(EscapeCsv(startStr)).Append(',');
            sb.Append(EscapeCsv(endStr)).Append(',');
            sb.Append(segment.Words.Count).Append(',');
            sb.Append(avgConfidence.ToString("F2")).Append(',');
            sb.AppendLine(EscapeCsv(fullText));
        }

        return sb.ToString();
    }

    public async Task ExportAsync(Transcript transcript, Stream outputStream, ExportOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        string content = Export(transcript, options);
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        await outputStream.WriteAsync(bytes, ct);
    }

    private static string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return "\"\"";
        }

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return $"\"{field}\"";
    }
}
