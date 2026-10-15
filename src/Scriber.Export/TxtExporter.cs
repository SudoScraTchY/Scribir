using System.Text;
using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Exports transcript as plain text with speaker labels and timestamps.
/// Implements PH1-EXPORT-001.
/// </summary>
public sealed class TxtExporter : ITranscriptExporter
{
    public string FormatName => "Plain Text";
    public string FileExtension => ".txt";
    public string MimeType => "text/plain";

    public string Export(Transcript transcript, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        options ??= new ExportOptions();

        var speakerMap = transcript.Speakers.ToDictionary(s => s.Id, s => s.DisplayName);
        var sb = new StringBuilder();

        foreach (var segment in transcript.Segments)
        {
            var text = string.Join(" ", segment.Words.Select(w => w.Text)).Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            if (options.IncludeTimestamps)
            {
                sb.Append('[');
                sb.Append(TimeFormatUtils.FormatHuman(segment.Start));
                sb.Append(" - ");
                sb.Append(TimeFormatUtils.FormatHuman(segment.End));
                sb.Append("] ");
            }

            if (options.IncludeSpeakerLabels)
            {
                string speakerName = speakerMap.TryGetValue(segment.SpeakerId, out var name)
                    ? name
                    : segment.SpeakerId;
                sb.Append(speakerName);
                sb.Append(": ");
            }

            sb.AppendLine(text);
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
}
