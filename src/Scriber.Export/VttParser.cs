using System.Text.RegularExpressions;

namespace Scriber.Export;

/// <summary>
/// Parsed subtitle cue from a WebVTT file.
/// </summary>
public record VttCue(TimeSpan Start, TimeSpan End, string Text, string? SpeakerLabel);

/// <summary>
/// Parser for validating and deserializing WebVTT subtitle content.
/// </summary>
public static class VttParser
{
    private static readonly Regex CueTimingRegex = new(
        @"^(\d{2}:)?(\d{2}:\d{2}\.\d{3})\s*-->\s*(\d{2}:)?(\d{2}:\d{2}\.\d{3})",
        RegexOptions.Compiled);

    private static readonly Regex VoiceTagRegex = new(
        @"^<v\s+([^>]+)>(.*)$",
        RegexOptions.Compiled);

    public static IReadOnlyList<VttCue> Parse(string vttContent)
    {
        ArgumentNullException.ThrowIfNull(vttContent);

        var lines = vttContent.Replace("\r\n", "\n").Split('\n');
        var cues = new List<VttCue>();

        int i = 0;
        // Check for WEBVTT header
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= lines.Length || !lines[i].Trim().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Missing WEBVTT header.");

        i++;

        while (i < lines.Length)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                i++;
                continue;
            }

            // Check if line is timing
            var match = CueTimingRegex.Match(line);
            if (!match.Success)
            {
                // Might be an optional cue identifier, peek next line
                if (i + 1 < lines.Length && CueTimingRegex.IsMatch(lines[i + 1].Trim()))
                {
                    i++;
                    match = CueTimingRegex.Match(lines[i].Trim());
                }
                else
                {
                    i++;
                    continue;
                }
            }

            var start = TimeFormatUtils.ParseVttTime(match.Groups[1].Value + match.Groups[2].Value);
            var end = TimeFormatUtils.ParseVttTime(match.Groups[3].Value + match.Groups[4].Value);

            i++;
            var textLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                textLines.Add(lines[i].Trim());
                i++;
            }

            string fullText = string.Join(" ", textLines).Trim();
            string? speaker = null;

            var voiceMatch = VoiceTagRegex.Match(fullText);
            if (voiceMatch.Success)
            {
                speaker = voiceMatch.Groups[1].Value.Trim();
                fullText = voiceMatch.Groups[2].Value.Trim();
            }

            cues.Add(new VttCue(start, end, fullText, speaker));
        }

        return cues;
    }
}
