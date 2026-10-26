using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Exports transcript to high-fidelity PDF format with speaker cards,
/// timestamps, confidence styling, and bi-directional RTL support for Persian text.
/// Implements PH1-EXPORT-003.
/// </summary>
public sealed class PdfExporter : ITranscriptExporter
{
    static PdfExporter()
    {
        // QuestPDF Community License for open-source / local application
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
    }

    public string FormatName => "PDF Document";
    public string FileExtension => ".pdf";
    public string MimeType => "application/pdf";

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

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x =>
                {
                    var style = x.FontSize(10)
                                 .FontColor(Colors.Grey.Darken4);
                    return isRtl ? style.FontFamily("Vazirmatn", "Segoe UI", "Tahoma") : style.FontFamily("Segoe UI", "Helvetica", "Arial");
                });

                if (isRtl)
                {
                    page.ContentFromRightToLeft();
                }

                // Page Header
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(transcript.Title)
                            .FontSize(20)
                            .Bold()
                            .FontColor("#0F172A");
                    });

                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span($"📅 {transcript.CreatedAt:yyyy-MM-dd HH:mm}   ").FontColor(Colors.Grey.Darken1).FontSize(9);
                            text.Span($"⏱️ {TimeFormatUtils.FormatHuman(transcript.Audio.Duration)}   ").FontColor(Colors.Grey.Darken1).FontSize(9);
                            text.Span($"🌐 {transcript.LanguageCode}   ").FontColor(Colors.Grey.Darken1).FontSize(9);
                            text.Span($"👥 {transcript.Speakers.Count} Speaker{(transcript.Speakers.Count == 1 ? "" : "s")}").FontColor(Colors.Grey.Darken1).FontSize(9);
                        });
                    });

                    col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                // Page Content (Transcript Segments)
                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(10);

                    foreach (var segment in transcript.Segments)
                    {
                        speakerMap.TryGetValue(segment.SpeakerId, out var speaker);
                        string speakerName = speaker?.DisplayName ?? segment.SpeakerId;
                        string speakerColorHex = speaker?.ColorHex ?? "#4F46E5";

                        col.Item().Border(1).BorderColor(Colors.Grey.Lighten3).Background("#F8FAFC").Padding(8).Column(card =>
                        {
                            card.Item().Row(r =>
                            {
                                if (options.IncludeSpeakerLabels)
                                {
                                    r.AutoItem().Background(speakerColorHex).PaddingHorizontal(6).PaddingVertical(2).Text(speakerName)
                                        .FontSize(9)
                                        .Bold()
                                        .FontColor(Colors.White);
                                }

                                if (options.IncludeTimestamps)
                                {
                                    r.RelativeItem().AlignRight().Text($"{TimeFormatUtils.FormatHuman(segment.Start)} - {TimeFormatUtils.FormatHuman(segment.End)}")
                                        .FontSize(8)
                                        .FontColor(Colors.Grey.Darken1);
                                }
                            });

                            card.Item().PaddingTop(6).Text(t =>
                            {
                                if (segment.Words.Count > 0)
                                {
                                    for (int i = 0; i < segment.Words.Count; i++)
                                    {
                                        var word = segment.Words[i];
                                        var span = t.Span(word.Text + (i == segment.Words.Count - 1 ? "" : " "));
                                        if (word.Confidence < 0.70)
                                        {
                                            span.FontColor(Colors.Amber.Darken3).Underline();
                                        }
                                        else
                                        {
                                            span.FontColor("#0F172A");
                                        }
                                    }
                                }
                                else
                                {
                                    t.Span(string.Join(" ", segment.Words.Select(w => w.Text))).FontColor("#0F172A");
                                }
                            });
                        });
                    }
                });

                // Page Footer
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text("Generated with Scribir • 100% On-Device AI Speech Transcription")
                        .FontSize(8)
                        .FontColor(Colors.Grey.Darken1);

                    row.RelativeItem().AlignRight().Text(x =>
                    {
                        x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                        x.Span(" / ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        x.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        });

        doc.GeneratePdf(outputStream);
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
