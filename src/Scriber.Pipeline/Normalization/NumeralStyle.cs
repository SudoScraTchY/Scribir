namespace Scriber.Pipeline.Normalization;

/// <summary>
/// Controls the script rendering style of numerals in transcript text.
/// </summary>
public enum NumeralStyle
{
    /// <summary>Keep digits in their original script as emitted by ASR.</summary>
    Unchanged,

    /// <summary>Convert all digits to Persian Eastern-Arabic numerals (۰ ۱ ۲ ۳ ۴ ۵ ۶ ۷ ۸ ۹).</summary>
    Persian,

    /// <summary>Convert all digits to standard Latin numerals (0 1 2 3 4 5 6 7 8 9).</summary>
    Latin
}
