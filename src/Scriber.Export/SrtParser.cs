using System.Text.RegularExpressions;

namespace Scriber.Export;

/// <summary>
/// Parsed subtitle cue from an SRT file.
/// </summary>
public record SrtCue(int Index, TimeSpan Start, TimeSpan End, string Text, string? SpeakerLabel);

/// <summary>
/// Parser for validating and deserializing SubRip (SRT) subtitle content.
/// </summary>
public static class SrtParser
{
    private static readonly Regex CueTimingRegex = new(
        @"^(\d{2}:\d{2}:\d{2},\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2},\d{3})",
        RegexOptions.Compiled);

    private static readonly Regex SpeakerPrefixRegex = new(
        @"^\[([^\]]+)\]\s*(.*)$",
        RegexOptions.Compiled);

    public static IReadOnlyList<SrtCue> Parse(string srtContent)
    {
        ArgumentNullException.ThrowIfNull(srtContent);

        var lines = srtContent.Replace("\r\n", "\n").Split('\n');
        var cues = new List<SrtCue>();

        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                i++;
                continue;
            }

            // 1. Cue Index
            if (!int.TryParse(line, out int cueIndex))
            {
                i++;
                continue;
            }

            i++;
            if (i >= lines.Length) break;

            // 2. Timing
            string timingLine = lines[i].Trim();
            var match = CueTimingRegex.Match(timingLine);
            if (!match.Success)
            {
                i++;
                continue;
            }

            var start = TimeFormatUtils.ParseSrtTime(match.Groups[1].Value);
            var end = TimeFormatUtils.ParseSrtTime(match.Groups[2].Value);

            i++;
            // 3. Text lines
            var textLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                textLines.Add(lines[i].Trim());
                i++;
            }

            string fullText = string.Join(" ", textLines).Trim();
            string? speaker = null;
            var speakerMatch = SpeakerPrefixRegex.Match(fullText);
            if (speakerMatch.Success)
            {
                speaker = speakerMatch.Groups[1].Value;
                fullText = speakerMatch.Groups[2].Value;
            }

            cues.Add(new SrtCue(cueIndex, start, end, fullText, speaker));
        }

        return cues;
    }
}
