namespace Scriber.Export;

/// <summary>
/// General configuration options for transcript exporting.
/// </summary>
public record ExportOptions
{
    /// <summary>Whether to include timestamps in human-readable formats (TXT, Markdown). Defaults to true.</summary>
    public bool IncludeTimestamps { get; init; } = true;

    /// <summary>Whether to include speaker display names. Defaults to true.</summary>
    public bool IncludeSpeakerLabels { get; init; } = true;

    /// <summary>Whether to include machine-readable metadata in Markdown (e.g. invisible word timestamps). Defaults to true.</summary>
    public bool IncludeMarkdownWordMetadata { get; init; } = true;
}
