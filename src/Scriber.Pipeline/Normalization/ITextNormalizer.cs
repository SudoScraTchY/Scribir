using Scriber.Core.Domain;

namespace Scriber.Pipeline.Normalization;

/// <summary>
/// Text normalization pass for cleaning transcript text, normalizing Persian script,
/// preserving zero-width non-joiners (ZWNJ), and formatting numerals.
/// Implements PH1-CORE-007.
/// </summary>
public interface ITextNormalizer
{
    /// <summary>Normalizes a text string per the provided options.</summary>
    string NormalizeText(string text, NormalizationOptions? options = null);

    /// <summary>Normalizes the text of a single word while preserving start/end timestamps and confidence.</summary>
    Word NormalizeWord(Word word, NormalizationOptions? options = null);

    /// <summary>Normalizes all words within a segment, maintaining domain invariants.</summary>
    Segment NormalizeSegment(Segment segment, NormalizationOptions? options = null);

    /// <summary>Normalizes an entire transcript segment list.</summary>
    IReadOnlyList<Segment> NormalizeSegments(IReadOnlyList<Segment> segments, NormalizationOptions? options = null);
}
