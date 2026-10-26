using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Exports transcript to Microsoft Word (.docx) format with speaker labels,
/// audio timestamps, and bi-directional RTL support for Persian text.
/// Implements PH1-EXPORT-003.
/// </summary>
public sealed class DocxExporter : ITranscriptExporter
{
    public string FormatName => "Microsoft Word Document";
    public string FileExtension => ".docx";
    public string MimeType => "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public string Export(Transcript transcript, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        using var ms = new MemoryStream();
        ExportAsync(transcript, ms, options).GetAwaiter().GetResult();
        return Convert.ToBase64String(ms.ToArray());
    }

    public Task ExportAsync(Transcript transcript, Stream outputStream, ExportOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(outputStream);
        ct.ThrowIfCancellationRequested();
        options ??= new ExportOptions();

        bool isRtl = IsRightToLeftLanguage(transcript.LanguageCode);
        var speakerMap = transcript.Speakers.ToDictionary(s => s.Id, s => s);

        using (var wordDoc = WordprocessingDocument.Create(outputStream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // 1. Document Title
            var titlePara = body.AppendChild(new Paragraph());
            var titlePPr = titlePara.AppendChild(new ParagraphProperties());
            if (isRtl)
            {
                titlePPr.AppendChild(new BiDi());
                titlePPr.AppendChild(new Justification { Val = JustificationValues.Right });
            }
            titlePPr.AppendChild(new SpacingBetweenLines { After = "240" }); // 12pt after

            var titleRun = titlePara.AppendChild(new Run());
            var titleRPr = titleRun.AppendChild(new RunProperties());
            titleRPr.AppendChild(new Bold());
            titleRPr.AppendChild(new FontSize { Val = "36" }); // 18pt
            titleRPr.AppendChild(new Color { Val = "1E293B" });
            if (isRtl)
            {
                titleRPr.AppendChild(new RightToLeftText());
                titleRPr.AppendChild(new RunFonts { ComplexScript = "Vazirmatn", Ascii = "Segoe UI" });
            }
            titleRun.AppendChild(new Text(transcript.Title));

            // 2. Metadata bar (Date, Duration, Language, Speakers)
            var metaPara = body.AppendChild(new Paragraph());
            var metaPPr = metaPara.AppendChild(new ParagraphProperties());
            if (isRtl)
            {
                metaPPr.AppendChild(new BiDi());
                metaPPr.AppendChild(new Justification { Val = JustificationValues.Right });
            }
            metaPPr.AppendChild(new SpacingBetweenLines { After = "360" }); // 18pt after

            string metaText = $"Date: {transcript.CreatedAt:yyyy-MM-dd HH:mm}   |   Duration: {TimeFormatUtils.FormatHuman(transcript.Audio.Duration)}   |   Language: {transcript.LanguageCode}   |   Participants: {transcript.Speakers.Count}";
            var metaRun = metaPara.AppendChild(new Run());
            var metaRPr = metaRun.AppendChild(new RunProperties());
            metaRPr.AppendChild(new FontSize { Val = "18" }); // 9pt
            metaRPr.AppendChild(new Color { Val = "64748B" });
            if (isRtl)
            {
                metaRPr.AppendChild(new RightToLeftText());
            }
            metaRun.AppendChild(new Text(metaText));

            // 3. Transcript Segments
            foreach (var segment in transcript.Segments)
            {
                var segPara = body.AppendChild(new Paragraph());
                var segPPr = segPara.AppendChild(new ParagraphProperties());
                if (isRtl)
                {
                    segPPr.AppendChild(new BiDi());
                    segPPr.AppendChild(new Justification { Val = JustificationValues.Right });
                }
                segPPr.AppendChild(new SpacingBetweenLines { After = "160", Line = "280", LineRule = LineSpacingRuleValues.Auto });

                // Speaker & Timestamp Header
                speakerMap.TryGetValue(segment.SpeakerId, out var speaker);
                string speakerName = speaker?.DisplayName ?? segment.SpeakerId;

                if (options.IncludeSpeakerLabels)
                {
                    var speakerRun = segPara.AppendChild(new Run());
                    var speakerRPr = speakerRun.AppendChild(new RunProperties());
                    speakerRPr.AppendChild(new Bold());
                    speakerRPr.AppendChild(new FontSize { Val = "22" }); // 11pt
                    speakerRPr.AppendChild(new Color { Val = "4F46E5" }); // Indigo
                    if (isRtl)
                    {
                        speakerRPr.AppendChild(new RightToLeftText());
                        speakerRPr.AppendChild(new RunFonts { ComplexScript = "Vazirmatn", Ascii = "Segoe UI" });
                    }
                    speakerRun.AppendChild(new Text(speakerName));
                }

                if (options.IncludeTimestamps)
                {
                    var timeRun = segPara.AppendChild(new Run());
                    var timeRPr = timeRun.AppendChild(new RunProperties());
                    timeRPr.AppendChild(new FontSize { Val = "18" }); // 9pt
                    timeRPr.AppendChild(new Color { Val = "94A3B8" }); // Muted gray
                    if (isRtl)
                    {
                        timeRPr.AppendChild(new RightToLeftText());
                    }
                    string timeText = $"  [{TimeFormatUtils.FormatHuman(segment.Start)} - {TimeFormatUtils.FormatHuman(segment.End)}]:  ";
                    timeRun.AppendChild(new Text(timeText) { Space = SpaceProcessingModeValues.Preserve });
                }
                else if (options.IncludeSpeakerLabels)
                {
                    var colonRun = segPara.AppendChild(new Run());
                    var colonRPr = colonRun.AppendChild(new RunProperties());
                    colonRPr.AppendChild(new Bold());
                    colonRun.AppendChild(new Text(": ") { Space = SpaceProcessingModeValues.Preserve });
                }

                // Segment Text
                string text = string.Join(" ", segment.Words.Select(w => w.Text)).Trim();
                if (string.IsNullOrEmpty(text))
                {
                    text = segment.Words.Count == 0 ? string.Empty : string.Join(" ", segment.Words.Select(w => w.Text));
                }

                var textRun = segPara.AppendChild(new Run());
                var textRPr = textRun.AppendChild(new RunProperties());
                textRPr.AppendChild(new FontSize { Val = "22" }); // 11pt
                textRPr.AppendChild(new Color { Val = "0F172A" });
                if (isRtl)
                {
                    textRPr.AppendChild(new RightToLeftText());
                    textRPr.AppendChild(new RunFonts { ComplexScript = "Vazirmatn", Ascii = "Segoe UI" });
                }
                textRun.AppendChild(new Text(text));
            }

            mainPart.Document.Save();
        }

        return Task.CompletedTask;
    }

    private static bool IsRightToLeftLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return false;
        string normalized = languageCode.Trim().ToLowerInvariant();
        return normalized.StartsWith("fa") ||
               normalized.StartsWith("ar") ||
               normalized.StartsWith("he") ||
               normalized.StartsWith("ur") ||
               normalized.StartsWith("per");
    }
}
