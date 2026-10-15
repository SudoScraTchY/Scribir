using System.Text;
using System.Text.RegularExpressions;
using Scriber.Core.Domain;

namespace Scriber.Pipeline.Normalization;

/// <summary>
/// Persian-aware text normalizer preserving zero-width non-joiners (ZWNJ \u200C),
/// standardizing Persian typography (ی / ک), formatting numerals, and cleaning punctuation spacing.
/// Implements PH1-CORE-007.
/// </summary>
public sealed class PersianTextNormalizer : ITextNormalizer
{
    public const char Zwnj = '\u200C';

    private static readonly char[] LatinDigits = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'];
    private static readonly char[] PersianDigits = ['۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹'];
    private static readonly char[] ArabicDigits = ['٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩'];

    public string NormalizeText(string text, NormalizationOptions? options = null)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        options ??= new NormalizationOptions();

        var sb = new StringBuilder(text);

        // 1. Character standardization (Arabic -> Persian standard characters)
        if (options.StandardizePersianCharacters)
        {
            sb.Replace('\u064A', '\u06CC'); // Arabic Yeh (ي) -> Persian Yeh (ی)
            sb.Replace('\u0649', '\u06CC'); // Arabic Alef Maksura (ى) -> Persian Yeh (ی)
            sb.Replace('\u0643', '\u06A9'); // Arabic Kaf (ك) -> Persian Keheh (ک)
            sb.Replace('\u0629', '\u0647'); // Arabic Teh Marbuta (ة) -> Persian Heh (ه)
            sb.Replace("\u06C0", "ه\u200Cی"); // Arabic Heh with Yeh above (ۀ) -> ه + ZWNJ + ی
        }

        // 2. Numeral style conversion
        if (options.NumeralStyle != NumeralStyle.Unchanged)
        {
            ConvertNumerals(sb, options.NumeralStyle);
        }

        var result = sb.ToString();

        // 3. Clean up punctuation and whitespace spacing without destroying ZWNJ
        if (options.CleanupPunctuationSpacing)
        {
            // Remove multiple horizontal spaces (excluding ZWNJ)
            result = Regex.Replace(result, @"[ \t]+", " ");

            // Remove spaces preceding punctuation marks
            result = Regex.Replace(result, @"[ \t]+([.,!?;:\u060C\u061F\u061B])", "$1");

            // Ensure space after punctuation if immediately adjacent to a word character
            result = Regex.Replace(result, @"([.,!?;:\u060C\u061F\u061B])(?=[^\s.,!?;:\u060C\u061F\u061B\d])", "$1 ");

            result = result.Trim();
        }

        return result;
    }

    public Word NormalizeWord(Word word, NormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(word);

        var normalizedText = NormalizeText(word.Text, options);
        if (string.IsNullOrEmpty(normalizedText))
            normalizedText = word.Text;

        // Preserve original timestamps and confidence score exactly
        return new Word(normalizedText, word.Start, word.End, word.Confidence);
    }

    public Segment NormalizeSegment(Segment segment, NormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var normalizedWords = segment.Words
            .Select(w => NormalizeWord(w, options))
            .ToList();

        // Domain invariant: words inside [Start, End]
        return new Segment(segment.SpeakerId, segment.Start, segment.End, normalizedWords);
    }

    public IReadOnlyList<Segment> NormalizeSegments(IReadOnlyList<Segment> segments, NormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return segments
            .Select(s => NormalizeSegment(s, options))
            .ToList();
    }

    private static void ConvertNumerals(StringBuilder sb, NumeralStyle targetStyle)
    {
        for (int i = 0; i < sb.Length; i++)
        {
            char c = sb[i];

            if (targetStyle == NumeralStyle.Persian)
            {
                // Convert Latin digits 0-9 to Persian ۰-۹
                if (c >= '0' && c <= '9')
                {
                    sb[i] = PersianDigits[c - '0'];
                }
                // Convert Arabic digits ٠-٩ to Persian ۰-۹
                else if (c >= '\u0660' && c <= '\u0669')
                {
                    sb[i] = PersianDigits[c - '\u0660'];
                }
            }
            else if (targetStyle == NumeralStyle.Latin)
            {
                // Convert Persian digits ۰-۹ to Latin 0-9
                if (c >= '\u06F0' && c <= '\u06F9')
                {
                    sb[i] = LatinDigits[c - '\u06F0'];
                }
                // Convert Arabic digits ٠-٩ to Latin 0-9
                else if (c >= '\u0660' && c <= '\u0669')
                {
                    sb[i] = LatinDigits[c - '\u0660'];
                }
            }
        }
    }
}
