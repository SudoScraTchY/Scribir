using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Parser and deserializer for Markdown transcripts with YAML frontmatter.
/// Supports lossless round-trip parsing back into Transcript / Segment / Word domain models.
/// Implements PH1-EXPORT-002.
/// </summary>
public static class MarkdownImporter
{
    private static readonly Regex FrontmatterRegex = new(
        @"^---\s*\n(.*?)\n---\s*\n",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HeadingRegex = new(
        @"^###\s+([^(]+)(?:\(([^)-]+)(?:\s*-\s*([^)]+))?\))?",
        RegexOptions.Compiled);

    private static readonly Regex MetadataCommentRegex = new(
        @"<!--\s*scriber:words:([^:]+):([^:]+):([^\s]+)\s+(.*?)\s*-->",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static Transcript Import(string markdown, Guid? id = null, string? audioPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        string normalized = markdown.Replace("\r\n", "\n");

        // 1. Parse Frontmatter
        var fmMatch = FrontmatterRegex.Match(normalized);
        string title = "Imported Transcript";
        var createdAt = DateTimeOffset.UtcNow;
        var duration = TimeSpan.FromMinutes(1);
        string languageCode = "en";
        var participantNames = new List<string>();

        string body = normalized;

        if (fmMatch.Success)
        {
            string fmText = fmMatch.Groups[1].Value;
            body = normalized[fmMatch.Length..];

            foreach (var rawLine in fmText.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                {
                    title = Unquote(line["title:".Length..].Trim());
                }
                else if (line.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTimeOffset.TryParse(Unquote(line["date:".Length..].Trim()), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        createdAt = dt;
                }
                else if (line.StartsWith("duration:", StringComparison.OrdinalIgnoreCase))
                {
                    duration = ParseDuration(Unquote(line["duration:".Length..].Trim()));
                }
                else if (line.StartsWith("language:", StringComparison.OrdinalIgnoreCase))
                {
                    languageCode = Unquote(line["language:".Length..].Trim());
                }
                else if (line.StartsWith("-", StringComparison.OrdinalIgnoreCase))
                {
                    participantNames.Add(Unquote(line[1..].Trim()));
                }
            }
        }

        // 2. Parse Speakers & Segments from Body
        var speakerMap = new Dictionary<string, Speaker>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < participantNames.Count; i++)
        {
            string name = participantNames[i];
            string sid = $"speaker-{i}";
            speakerMap[name] = new Speaker(sid, name, "#2563EB");
            speakerMap[sid] = speakerMap[name];
        }

        var segments = new List<Segment>();
        var blocks = Regex.Split(body, @"(?=^###\s+)", RegexOptions.Multiline);

        foreach (var block in blocks)
        {
            var trimmed = block.Trim();
            if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith("###"))
                continue;

            var lines = trimmed.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
            if (lines.Count == 0) continue;

            string headingLine = lines[0];
            var hMatch = HeadingRegex.Match(headingLine);
            if (!hMatch.Success) continue;

            string speakerLabel = hMatch.Groups[1].Value.Trim();
            TimeSpan segStart = TimeSpan.Zero;
            TimeSpan segEnd = TimeSpan.FromSeconds(5);

            if (hMatch.Groups[2].Success && !string.IsNullOrWhiteSpace(hMatch.Groups[2].Value))
            {
                segStart = ParseTimeHuman(hMatch.Groups[2].Value.Trim());
            }
            if (hMatch.Groups[3].Success && !string.IsNullOrWhiteSpace(hMatch.Groups[3].Value))
            {
                segEnd = ParseTimeHuman(hMatch.Groups[3].Value.Trim());
            }

            // Find or create speaker
            if (!speakerMap.TryGetValue(speakerLabel, out var speaker))
            {
                string newId = $"speaker-{speakerMap.Count}";
                speaker = new Speaker(newId, speakerLabel, "#2563EB");
                speakerMap[speakerLabel] = speaker;
                speakerMap[newId] = speaker;
            }

            // Check for metadata comment
            var commentMatch = MetadataCommentRegex.Match(trimmed);
            List<Word> words = [];

            if (commentMatch.Success)
            {
                string jsonWords = commentMatch.Groups[4].Value;
                try
                {
                    var wordDtos = JsonSerializer.Deserialize<List<WordJsonDto>>(jsonWords, JsonExporter.JsonOptions);
                    if (wordDtos != null)
                    {
                        words = wordDtos.Select(w => new Word(
                            w.Text,
                            TimeSpan.FromSeconds(w.StartSeconds),
                            TimeSpan.FromSeconds(w.EndSeconds),
                            w.Confidence)).ToList();
                    }
                }
                catch
                {
                    // Fall back to plain text below
                }
            }

            if (words.Count == 0)
            {
                // Extract plain text lines (excluding heading and comments)
                var textLines = lines.Skip(1).Where(l => !l.StartsWith("<!--") && !l.EndsWith("-->")).ToList();
                string fullText = string.Join(" ", textLines).Trim();
                var tokens = fullText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length > 0)
                {
                    double totalSec = (segEnd - segStart).TotalSeconds;
                    double perWord = Math.Max(0.05, totalSec / tokens.Length);
                    for (int w = 0; w < tokens.Length; w++)
                    {
                        var wStart = segStart + TimeSpan.FromSeconds(w * perWord);
                        var wEnd = segStart + TimeSpan.FromSeconds((w + 1) * perWord);
                        if (wEnd > segEnd) wEnd = segEnd;
                        if (wEnd <= wStart) wEnd = wStart + TimeSpan.FromMilliseconds(50);
                        words.Add(new Word(tokens[w], wStart, wEnd, 0.90));
                    }
                }
            }

            if (words.Count > 0)
            {
                var finalStart = words[0].Start;
                var finalEnd = words[^1].End;
                if (finalEnd <= finalStart) finalEnd = finalStart + TimeSpan.FromMilliseconds(50);
                segments.Add(new Segment(speaker.Id, finalStart, finalEnd, words));
            }
        }

        // Deduplicate distinct speakers
        var distinctSpeakers = speakerMap.Values
            .DistinctBy(s => s.Id)
            .ToList();

        if (distinctSpeakers.Count == 0 && segments.Count > 0)
        {
            distinctSpeakers.Add(new Speaker("speaker-0", "Speaker 1", "#2563EB"));
        }

        var audioRef = new AudioReference(
            localPath: audioPath ?? "recording.wav",
            duration: duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(1),
            sampleRate: 16000);

        return new Transcript(
            id: id ?? Guid.NewGuid(),
            title: title,
            createdAt: createdAt,
            languageCode: languageCode,
            speakers: distinctSpeakers,
            segments: segments,
            audio: audioRef);
    }

    private static string Unquote(string s)
    {
        if (s.StartsWith('"') && s.EndsWith('"') && s.Length >= 2)
            return s[1..^1].Replace("\\\"", "\"");
        return s;
    }

    private static TimeSpan ParseDuration(string s)
    {
        var parts = s.Split(':');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out int h) &&
            int.TryParse(parts[1], out int m) &&
            int.TryParse(parts[2], out int sec))
        {
            return new TimeSpan(h, m, sec);
        }
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int m2) &&
            int.TryParse(parts[1], out int sec2))
        {
            return new TimeSpan(0, m2, sec2);
        }
        return TimeSpan.FromMinutes(1);
    }

    private static TimeSpan ParseTimeHuman(string s)
    {
        var parts = s.Split(':');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out int h) &&
            int.TryParse(parts[1], out int m) &&
            double.TryParse(parts[2], CultureInfo.InvariantCulture, out double sec))
        {
            return TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(sec);
        }
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int m2) &&
            double.TryParse(parts[1], CultureInfo.InvariantCulture, out double sec2))
        {
            return TimeSpan.FromMinutes(m2) + TimeSpan.FromSeconds(sec2);
        }
        return TimeSpan.Zero;
    }
}
