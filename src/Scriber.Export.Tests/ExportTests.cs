using Scriber.Core.Domain;
using Xunit;

namespace Scriber.Export.Tests;

public class ExportTests
{
    private static Transcript CreateFixtureTranscript()
    {
        var speakers = new List<Speaker>
        {
            new("speaker-0", "Alice", "#2563EB"),
            new("speaker-1", "Bob", "#16A34A")
        };

        var seg1Words = new List<Word>
        {
            new("Hello", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.5), 0.95),
            new("world,", TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(2.0), 0.92),
            new("welcome.", TimeSpan.FromSeconds(2.1), TimeSpan.FromSeconds(2.8), 0.90)
        };

        var seg2Words = new List<Word>
        {
            new("Thank", TimeSpan.FromSeconds(3.0), TimeSpan.FromSeconds(3.3), 0.88),
            new("you", TimeSpan.FromSeconds(3.4), TimeSpan.FromSeconds(3.6), 0.93),
            new("Alice.", TimeSpan.FromSeconds(3.7), TimeSpan.FromSeconds(4.2), 0.91)
        };

        var segments = new List<Segment>
        {
            new("speaker-0", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(2.8), seg1Words),
            new("speaker-1", TimeSpan.FromSeconds(3.0), TimeSpan.FromSeconds(4.2), seg2Words)
        };

        var audio = new AudioReference(
            localPath: "test_audio.wav",
            duration: TimeSpan.FromSeconds(5.0),
            sampleRate: 16000);

        return new Transcript(
            id: Guid.NewGuid(),
            title: "Podcasting Interview",
            createdAt: new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.Zero),
            languageCode: "en",
            speakers: speakers,
            segments: segments,
            audio: audio);
    }

    [Fact]
    public void TxtExporter_DefaultOptions_IncludesTimestampsAndSpeakers()
    {
        var transcript = CreateFixtureTranscript();
        var exporter = new TxtExporter();

        string text = exporter.Export(transcript);

        Assert.Contains("[00:01 - 00:02] Alice: Hello world, welcome.", text);
        Assert.Contains("[00:03 - 00:04] Bob: Thank you Alice.", text);
    }

    [Fact]
    public void TxtExporter_WithoutTimestamps_OmitsBrackets()
    {
        var transcript = CreateFixtureTranscript();
        var exporter = new TxtExporter();
        var options = new ExportOptions { IncludeTimestamps = false };

        string text = exporter.Export(transcript, options);

        Assert.DoesNotContain("[", text);
        Assert.Contains("Alice: Hello world, welcome.", text);
        Assert.Contains("Bob: Thank you Alice.", text);
    }

    [Fact]
    public void SrtExporter_And_Parser_RoundTripCuesAndTimings()
    {
        var transcript = CreateFixtureTranscript();
        var exporter = new SrtExporter();

        string srt = exporter.Export(transcript);

        Assert.Contains("1\n00:00:01,000 --> 00:00:02,800", srt.Replace("\r\n", "\n"));
        Assert.Contains("[Alice] Hello world, welcome.", srt);
        Assert.Contains("2\n00:00:03,000 --> 00:00:04,200", srt.Replace("\r\n", "\n"));
        Assert.Contains("[Bob] Thank you Alice.", srt);

        // Validate parsing
        var cues = SrtParser.Parse(srt);
        Assert.Equal(2, cues.Count);

        Assert.Equal(1, cues[0].Index);
        Assert.Equal(TimeSpan.FromSeconds(1.0), cues[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(2.8), cues[0].End);
        Assert.Equal("Alice", cues[0].SpeakerLabel);
        Assert.Equal("Hello world, welcome.", cues[0].Text);

        Assert.Equal(2, cues[1].Index);
        Assert.Equal(TimeSpan.FromSeconds(3.0), cues[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(4.2), cues[1].End);
        Assert.Equal("Bob", cues[1].SpeakerLabel);
        Assert.Equal("Thank you Alice.", cues[1].Text);
    }

    [Fact]
    public void VttExporter_And_Parser_RoundTripCuesAndTimings()
    {
        var transcript = CreateFixtureTranscript();
        var exporter = new VttExporter();

        string vtt = exporter.Export(transcript);

        Assert.StartsWith("WEBVTT", vtt.Trim());
        Assert.Contains("00:00:01.000 --> 00:00:02.800", vtt);
        Assert.Contains("<v Alice>Hello world, welcome.", vtt);
        Assert.Contains("00:00:03.000 --> 00:00:04.200", vtt);
        Assert.Contains("<v Bob>Thank you Alice.", vtt);

        // Validate parsing
        var cues = VttParser.Parse(vtt);
        Assert.Equal(2, cues.Count);

        Assert.Equal(TimeSpan.FromSeconds(1.0), cues[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(2.8), cues[0].End);
        Assert.Equal("Alice", cues[0].SpeakerLabel);
        Assert.Equal("Hello world, welcome.", cues[0].Text);

        Assert.Equal(TimeSpan.FromSeconds(3.0), cues[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(4.2), cues[1].End);
        Assert.Equal("Bob", cues[1].SpeakerLabel);
        Assert.Equal("Thank you Alice.", cues[1].Text);
    }

    [Fact]
    public void JsonExporter_RoundTrip_RestoresExactDomainModel()
    {
        var transcript = CreateFixtureTranscript();
        var exporter = new JsonExporter();

        string json = exporter.Export(transcript);
        Assert.Contains("\"schema_version\": \"1.0\"", json);
        Assert.Contains("\"Podcasting Interview\"", json);

        var restored = JsonExporter.Import(json);

        Assert.Equal(transcript.Id, restored.Id);
        Assert.Equal(transcript.Title, restored.Title);
        Assert.Equal(transcript.CreatedAt, restored.CreatedAt);
        Assert.Equal(transcript.LanguageCode, restored.LanguageCode);
        Assert.Equal(transcript.Audio.LocalPath, restored.Audio.LocalPath);
        Assert.Equal(transcript.Audio.Duration, restored.Audio.Duration);
        Assert.Equal(transcript.Audio.SampleRate, restored.Audio.SampleRate);

        Assert.Equal(transcript.Speakers.Count, restored.Speakers.Count);
        for (int i = 0; i < transcript.Speakers.Count; i++)
        {
            Assert.Equal(transcript.Speakers[i].Id, restored.Speakers[i].Id);
            Assert.Equal(transcript.Speakers[i].DisplayName, restored.Speakers[i].DisplayName);
            Assert.Equal(transcript.Speakers[i].ColorHex, restored.Speakers[i].ColorHex);
        }

        Assert.Equal(transcript.Segments.Count, restored.Segments.Count);
        for (int s = 0; s < transcript.Segments.Count; s++)
        {
            var origSeg = transcript.Segments[s];
            var restSeg = restored.Segments[s];

            Assert.Equal(origSeg.SpeakerId, restSeg.SpeakerId);
            Assert.Equal(origSeg.Start, restSeg.Start);
            Assert.Equal(origSeg.End, restSeg.End);
            Assert.Equal(origSeg.Words.Count, restSeg.Words.Count);

            for (int w = 0; w < origSeg.Words.Count; w++)
            {
                Assert.Equal(origSeg.Words[w].Text, restSeg.Words[w].Text);
                Assert.Equal(origSeg.Words[w].Start, restSeg.Words[w].Start);
                Assert.Equal(origSeg.Words[w].End, restSeg.Words[w].End);
                Assert.Equal(origSeg.Words[w].Confidence, restSeg.Words[w].Confidence);
            }
        }
    }

    [Fact]
    public void MarkdownExporter_And_Importer_RoundTripLosslessly()
    {
        var transcript = CreateFixtureTranscript();
        var exporter = new MarkdownExporter();

        string md = exporter.Export(transcript);

        // Frontmatter verification
        Assert.Contains("---", md);
        Assert.Contains("title: \"Podcasting Interview\"", md);
        Assert.Contains("language: \"en\"", md);
        Assert.Contains("- \"Alice\"", md);
        Assert.Contains("- \"Bob\"", md);

        // Body verification
        Assert.Contains("### Alice (00:01 - 00:02)", md);
        Assert.Contains("Hello world, welcome.", md);
        Assert.Contains("### Bob (00:03 - 00:04)", md);
        Assert.Contains("Thank you Alice.", md);
        Assert.Contains("<!-- scriber:words:speaker-0:", md);

        // Lossless round-trip import
        var imported = MarkdownImporter.Import(md, id: transcript.Id, audioPath: transcript.Audio.LocalPath);

        Assert.Equal(transcript.Id, imported.Id);
        Assert.Equal(transcript.Title, imported.Title);
        Assert.Equal(transcript.LanguageCode, imported.LanguageCode);
        Assert.Equal(2, imported.Segments.Count);

        // Segment 1
        var seg1 = imported.Segments[0];
        Assert.Equal(3, seg1.Words.Count);
        Assert.Equal("Hello", seg1.Words[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(1.0), seg1.Words[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(1.5), seg1.Words[0].End);
        Assert.Equal(0.95, seg1.Words[0].Confidence);

        // Segment 2
        var seg2 = imported.Segments[1];
        Assert.Equal(3, seg2.Words.Count);
        Assert.Equal("Thank", seg2.Words[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(3.0), seg2.Words[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(3.3), seg2.Words[0].End);
        Assert.Equal(0.88, seg2.Words[0].Confidence);
    }

    [Fact]
    public void MarkdownImporter_WithoutMetadataComments_ReconstructsCleanly()
    {
        string rawMarkdown = """
        ---
        title: "Simple Notes"
        date: 2026-10-15T20:00:00Z
        duration: "00:01:00"
        language: "en"
        participants:
          - "Charlie"
        ---

        ### Charlie (00:00 - 00:04)
        This is hand edited markdown text without any html comments.
        """;

        var imported = MarkdownImporter.Import(rawMarkdown);

        Assert.Equal("Simple Notes", imported.Title);
        Assert.Single(imported.Segments);
        Assert.Equal(TimeSpan.Zero, imported.Segments[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(4), imported.Segments[0].End);
        Assert.True(imported.Segments[0].Words.Count > 0);
        Assert.Equal("This", imported.Segments[0].Words[0].Text);
    }

    [Fact]
    public void HtmlExporter_ExportsValidHtmlWithSpeakersAndTimestamps()
    {
        var transcript = CreateFixtureTranscript();
        var exporter = new HtmlExporter();

        string html = exporter.Export(transcript);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("Podcasting Interview", html);
        Assert.Contains("Alice", html);
        Assert.Contains("Bob", html);
        Assert.Contains("Hello world, welcome.", html);
        Assert.Contains("Thank you Alice.", html);
    }

    [Fact]
    public void CsvExporter_ExportsValidCsvHeaderAndRows()
    {
        var transcript = CreateFixtureTranscript();
        var exporter = new CsvExporter();

        string csv = exporter.Export(transcript);

        Assert.Contains("Index,SpeakerId,SpeakerName,StartSeconds,EndSeconds,StartTime,EndTime,WordCount,AvgConfidence,Text", csv);
        Assert.Contains("Alice", csv);
        Assert.Contains("Bob", csv);
        Assert.Contains("\"Hello world, welcome.\"", csv);
    }
}
