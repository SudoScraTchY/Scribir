namespace Scriber.Pipeline.Normalization;

/// <summary>
/// User-configurable options for transcript text normalization.
/// </summary>
public sealed record NormalizationOptions
{
    /// <summary>Configures Persian vs Latin numeral rendering.</summary>
    public NumeralStyle NumeralStyle { get; init; } = NumeralStyle.Unchanged;

    /// <summary>Standardizes Arabic Yeh/Kaf to standard Persian Yeh/Keheh (ی / ک).</summary>
    public bool StandardizePersianCharacters { get; init; } = true;

    /// <summary>Cleans up erroneous whitespace around punctuation without altering word tokens.</summary>
    public bool CleanupPunctuationSpacing { get; init; } = true;

    /// <summary>Ensures zero-width non-joiner (ZWNJ \u200C) characters are strictly preserved.</summary>
    public bool PreserveZwnj { get; init; } = true;
}
