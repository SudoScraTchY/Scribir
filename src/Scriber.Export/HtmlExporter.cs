using System.Net;
using System.Text;
using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Exports transcript as a self-contained, responsive HTML document with embedded CSS,
/// speaker avatar badges, audio timestamps, and optional confidence highlighting.
/// </summary>
public sealed class HtmlExporter : ITranscriptExporter
{
    public string FormatName => "HTML Document";
    public string FileExtension => ".html";
    public string MimeType => "text/html";

    public string Export(Transcript transcript, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        options ??= new ExportOptions();

        var speakerMap = transcript.Speakers.ToDictionary(s => s.Id, s => s);
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"" + WebUtility.HtmlEncode(transcript.LanguageCode) + "\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>{WebUtility.HtmlEncode(transcript.Title)} - Scribir Transcript</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine(@"    :root {
      --bg: #0f172a;
      --card-bg: #1e293b;
      --border: #334155;
      --text: #f8fafc;
      --text-muted: #94a3b8;
      --primary: #6366f1;
      --accent: #38bdf8;
      --warn: #f59e0b;
    }
    @media (prefers-color-scheme: light) {
      :root {
        --bg: #f8fafc;
        --card-bg: #ffffff;
        --border: #e2e8f0;
        --text: #0f172a;
        --text-muted: #64748b;
        --primary: #4f46e5;
        --accent: #0284c7;
        --warn: #d97706;
      }
    }
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
      background: var(--bg);
      color: var(--text);
      line-height: 1.6;
      margin: 0;
      padding: 24px 16px;
    }
    .container {
      max-width: 800px;
      margin: 0 auto;
    }
    header {
      margin-bottom: 32px;
      padding-bottom: 20px;
      border-bottom: 1px solid var(--border);
    }
    h1 {
      margin: 0 0 12px 0;
      font-size: 26px;
      color: var(--text);
    }
    .meta-bar {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      font-size: 13px;
      color: var(--text-muted);
    }
    .meta-item {
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }
    .segment-card {
      background: var(--card-bg);
      border: 1px solid var(--border);
      border-radius: 12px;
      padding: 16px 20px;
      margin-bottom: 16px;
      transition: transform 0.15s ease, box-shadow 0.15s ease;
    }
    .segment-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 10px;
    }
    .speaker-pill {
      background: var(--primary);
      color: #ffffff;
      padding: 3px 10px;
      border-radius: 9999px;
      font-size: 12px;
      font-weight: 600;
    }
    .timestamp {
      font-size: 12px;
      color: var(--text-muted);
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
    }
    .segment-text {
      font-size: 15px;
      margin: 0;
      color: var(--text);
    }
    .word-low-conf {
      border-bottom: 2px dashed var(--warn);
      color: var(--warn);
      cursor: help;
    }
    footer {
      margin-top: 48px;
      text-align: center;
      font-size: 12px;
      color: var(--text-muted);
    }
");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <div class=\"container\">");

        // Header
        sb.AppendLine("    <header>");
        sb.AppendLine($"      <h1>{WebUtility.HtmlEncode(transcript.Title)}</h1>");
        sb.AppendLine("      <div class=\"meta-bar\">");
        sb.AppendLine($"        <span class=\"meta-item\">📅 {transcript.CreatedAt:yyyy-MM-dd HH:mm}</span>");
        sb.AppendLine($"        <span class=\"meta-item\">⏱️ {TimeFormatUtils.FormatHuman(transcript.Audio.Duration)}</span>");
        sb.AppendLine($"        <span class=\"meta-item\">🌐 {WebUtility.HtmlEncode(transcript.LanguageCode)}</span>");
        sb.AppendLine($"        <span class=\"meta-item\">👥 {transcript.Speakers.Count} Speaker{(transcript.Speakers.Count == 1 ? "" : "s")}</span>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </header>");

        // Segments
        sb.AppendLine("    <main>");
        foreach (var segment in transcript.Segments)
        {
            speakerMap.TryGetValue(segment.SpeakerId, out var speaker);
            string speakerName = speaker?.DisplayName ?? segment.SpeakerId;
            string startStr = TimeFormatUtils.FormatHuman(segment.Start);
            string endStr = TimeFormatUtils.FormatHuman(segment.End);

            sb.AppendLine("      <article class=\"segment-card\">");
            sb.AppendLine("        <div class=\"segment-header\">");
            sb.AppendLine($"          <span class=\"speaker-pill\">{WebUtility.HtmlEncode(speakerName)}</span>");
            if (options.IncludeTimestamps)
            {
                sb.AppendLine($"          <span class=\"timestamp\">{startStr} - {endStr}</span>");
            }
            sb.AppendLine("        </div>");

            sb.Append("        <p class=\"segment-text\">");
            if (segment.Words.Count > 0)
            {
                var wordTokens = new List<string>(segment.Words.Count);
                foreach (var w in segment.Words)
                {
                    string encodedWord = WebUtility.HtmlEncode(w.Text);
                    if (w.Confidence < 0.70)
                    {
                        wordTokens.Add($"<span class=\"word-low-conf\" title=\"Confidence: {w.Confidence:P0}\">{encodedWord}</span>");
                    }
                    else
                    {
                        wordTokens.Add(encodedWord);
                    }
                }
                sb.Append(string.Join(" ", wordTokens));
            }
            else
            {
                sb.Append(WebUtility.HtmlEncode(string.Join(" ", segment.Words.Select(w => w.Text))));
            }
            sb.AppendLine("</p>");
            sb.AppendLine("      </article>");
        }
        sb.AppendLine("    </main>");

        // Footer
        sb.AppendLine("    <footer>");
        sb.AppendLine("      Generated with Scribir &bull; 100% On-Device AI Speech Transcription");
        sb.AppendLine("    </footer>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    public async Task ExportAsync(Transcript transcript, Stream outputStream, ExportOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        string content = Export(transcript, options);
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        await outputStream.WriteAsync(bytes, ct);
    }
}
