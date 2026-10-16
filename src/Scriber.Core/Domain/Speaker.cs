namespace Scriber.Core.Domain;

using System.Text.RegularExpressions;

/// <summary>A speaker label used across a whole transcript.</summary>
public sealed record Speaker
{
    private static readonly Regex ColorHexRegex =
        new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>Stable identifier, unique within the transcript.</summary>
    public string Id { get; init; }

    /// <summary>Human-readable label (e.g. "Speaker 1").</summary>
    public string DisplayName { get; init; }

    /// <summary>UI color, #RRGGBB.</summary>
    public string ColorHex { get; init; }

    /// <summary>Optional linked cross-session voiceprint ID (ADR-0010).</summary>
    public Guid? VoiceprintId { get; init; }

    public Speaker(string id, string displayName, string colorHex, Guid? voiceprintId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName, nameof(displayName));
        if (!ColorHexRegex.IsMatch(colorHex))
        {
            throw new ArgumentException("ColorHex must be a #RRGGBB hex color.", nameof(colorHex));
        }

        Id = id;
        DisplayName = displayName;
        ColorHex = colorHex;
        VoiceprintId = voiceprintId;
    }
}
