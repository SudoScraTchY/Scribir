using System.Text;
using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Exports transcript as SubRip (SRT) subtitle file with speaker cues.
/// Implements PH1-EXPORT-001.
/// </summary>
public sealed class SrtExporter : ITranscriptExporter
{
    public string FormatName => "SubRip Subtitles";
    public string FileExtension => ".srt";
    public string MimeType => "application/x-subrip";

    public string Export(Transcript transcript, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        options ??= new ExportOptions();

        var speakerMap = transcript.Speakers.ToDictionary(s => s.Id, s => s.DisplayName);
        var sb = new StringBuilder();
        int cueIndex = 1;

        foreach (var segment in transcript.Segments)
        {
            var text = string.Join(" ", segment.Words.Select(w => w.Text)).Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            sb.AppendLine(cueIndex.ToString());
            sb.Append(TimeFormatUtils.FormatSrt(segment.Start));
            sb.Append(" --> ");
            sb.AppendLine(TimeFormatUtils.FormatSrt(segment.End));

            if (options.IncludeSpeakerLabels)
            {
                string speakerName = speakerMap.TryGetValue(segment.SpeakerId, out var name)
                    ? name
                    : segment.SpeakerId;
                sb.Append('[');
                sb.Append(speakerName);
                sb.Append("] ");
            }

            sb.AppendLine(text);
            sb.AppendLine();
            cueIndex++;
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
