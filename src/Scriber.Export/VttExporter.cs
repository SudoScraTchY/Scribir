using System.Text;
using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Exports transcript as WebVTT (VTT) subtitle file with speaker voice tags.
/// Implements PH1-EXPORT-001.
/// </summary>
public sealed class VttExporter : ITranscriptExporter
{
    public string FormatName => "WebVTT Subtitles";
    public string FileExtension => ".vtt";
    public string MimeType => "text/vtt";

    public string Export(Transcript transcript, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        options ??= new ExportOptions();

        var speakerMap = transcript.Speakers.ToDictionary(s => s.Id, s => s.DisplayName);
        var sb = new StringBuilder();

        sb.AppendLine("WEBVTT");
        sb.AppendLine();

        foreach (var segment in transcript.Segments)
        {
            var text = string.Join(" ", segment.Words.Select(w => w.Text)).Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            sb.Append(TimeFormatUtils.FormatVtt(segment.Start));
            sb.Append(" --> ");
            sb.AppendLine(TimeFormatUtils.FormatVtt(segment.End));

            if (options.IncludeSpeakerLabels)
            {
                string speakerName = speakerMap.TryGetValue(segment.SpeakerId, out var name)
                    ? name
                    : segment.SpeakerId;
                sb.Append("<v ");
                sb.Append(speakerName);
                sb.Append('>');
                sb.Append(text);
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine(text);
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
}
