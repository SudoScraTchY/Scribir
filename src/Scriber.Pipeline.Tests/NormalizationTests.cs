using Scriber.Core.Domain;
using Scriber.Pipeline.Normalization;
using Xunit;

namespace Scriber.Pipeline.Tests;

public class NormalizationTests
{
    private static Word W(string text, double startSec, double endSec, double conf = 0.95) =>
        new(text, TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec), conf);

    [Fact]
    public void NormalizeText_PreservesZwnjInPersianCompounds()
    {
        var normalizer = new PersianTextNormalizer();

        // Persian compounds with ZWNJ (\u200C)
        var compounds = new[]
        {
            "می\u200Cخواهم",  // می‌خواهم (I want)
            "نمی\u200Cدانم",   // نمی‌دانم (I don't know)
            "خانه\u200Cها",   // خانه‌ها (houses)
            "کتاب\u200Cهایم", // کتاب‌هایم (my books)
            "رفته\u200Cاند",  // رفته‌اند (they have gone)
            "زیباترین"        // standard without ZWNJ
        };

        foreach (var compound in compounds)
        {
            var normalized = normalizer.NormalizeText(compound);
            Assert.Equal(compound, normalized);

            if (compound.Contains('\u200C'))
            {
                Assert.Contains('\u200C', normalized);
            }
        }
    }

    [Fact]
    public void NormalizeText_StandardizesArabicLettersToPersian()
    {
        var normalizer = new PersianTextNormalizer();

        // Arabic Yeh (ي, \u064A) and Alef Maksura (ى, \u0649) -> Persian Yeh (ی, \u06CC)
        // Arabic Kaf (ك, \u0643) -> Persian Keheh (ک, \u06A9)
        string input = "يك كودك عربي";
        string expected = "یک کودک عربی";

        string actual = normalizer.NormalizeText(input);

        Assert.Equal(expected, actual);
        Assert.DoesNotContain('\u064A', actual);
        Assert.DoesNotContain('\u0643', actual);
    }

    [Fact]
    public void NormalizeText_NumeralStyle_Persian_ConvertsAllDigits()
    {
        var normalizer = new PersianTextNormalizer();
        var options = new NormalizationOptions { NumeralStyle = NumeralStyle.Persian };

        // Latin digits 0-9
        string latin = "سال 2026 میلادی و 1405 شمسی";
        string expected = "سال ۲۰۲۶ میلادی و ۱۴۰۵ شمسی";

        string actual = normalizer.NormalizeText(latin, options);
        Assert.Equal(expected, actual);

        // Arabic digits ٠-٩ -> Persian digits ۰-۹
        string arabicDigits = "رقم ١٢٣٤٥٦٧٨٩٠";
        string expectedPersian = "رقم ۱۲۳۴۵۶۷۸۹۰";

        Assert.Equal(expectedPersian, normalizer.NormalizeText(arabicDigits, options));
    }

    [Fact]
    public void NormalizeText_NumeralStyle_Latin_ConvertsAllDigits()
    {
        var normalizer = new PersianTextNormalizer();
        var options = new NormalizationOptions { NumeralStyle = NumeralStyle.Latin };

        string persian = "سال ۲۰۲۶ میلادی و ۱۴۰۵ شمسی";
        string expected = "سال 2026 میلادی و 1405 شمسی";

        string actual = normalizer.NormalizeText(persian, options);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeText_NumeralStyle_Unchanged_PreservesOriginalScript()
    {
        var normalizer = new PersianTextNormalizer();
        var options = new NormalizationOptions { NumeralStyle = NumeralStyle.Unchanged };

        string mixed = "عدد 123 و عدد ۴۵۶";
        string actual = normalizer.NormalizeText(mixed, options);

        Assert.Equal(mixed, actual);
    }

    [Fact]
    public void NormalizeText_CleansPunctuationSpacingWithoutCorruptingWords()
    {
        var normalizer = new PersianTextNormalizer();

        string messy = "سلام   ،  حال شما چطور است  ؟  خوبم  . ممنون  !";
        string expected = "سلام، حال شما چطور است؟ خوبم. ممنون!";

        string actual = normalizer.NormalizeText(messy);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeWord_StrictlyPreservesTimestampsAndConfidence()
    {
        var normalizer = new PersianTextNormalizer();
        var options = new NormalizationOptions { NumeralStyle = NumeralStyle.Persian };

        var word = W("كتاب 2", 1.25, 2.75, 0.88);
        var normalized = normalizer.NormalizeWord(word, options);

        Assert.Equal("کتاب ۲", normalized.Text);
        Assert.Equal(TimeSpan.FromSeconds(1.25), normalized.Start);
        Assert.Equal(TimeSpan.FromSeconds(2.75), normalized.End);
        Assert.Equal(0.88, normalized.Confidence);
    }

    [Fact]
    public void NormalizeSegment_PreservesDomainInvariantsAndMetadata()
    {
        var normalizer = new PersianTextNormalizer();
        var options = new NormalizationOptions { NumeralStyle = NumeralStyle.Persian };

        var words = new[]
        {
            W("من", 1.0, 1.5, 0.95),
            W("مي\u200Cخواهم", 1.6, 2.3, 0.92), // Arabic Yeh
            W("2", 2.4, 2.8, 0.85),           // Latin digit
            W("كتاب", 2.9, 3.5, 0.90)          // Arabic Kaf
        };

        var segment = new Segment("speaker-0", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(4.0), words);
        var normalized = normalizer.NormalizeSegment(segment, options);

        Assert.Equal("speaker-0", normalized.SpeakerId);
        Assert.Equal(TimeSpan.FromSeconds(1.0), normalized.Start);
        Assert.Equal(TimeSpan.FromSeconds(4.0), normalized.End);
        Assert.Equal(4, normalized.Words.Count);

        Assert.Equal("من", normalized.Words[0].Text);
        Assert.Equal("می\u200Cخواهم", normalized.Words[1].Text);
        Assert.Equal("۲", normalized.Words[2].Text);
        Assert.Equal("کتاب", normalized.Words[3].Text);

        // Verify ZWNJ preserved
        Assert.Contains('\u200C', normalized.Words[1].Text);

        // Verify domain invariants
        Assert.True(normalized.End > normalized.Start);
        foreach (var w in normalized.Words)
        {
            Assert.True(w.Start >= normalized.Start);
            Assert.True(w.End <= normalized.End);
            Assert.InRange(w.Confidence, 0.0, 1.0);
        }
    }
}
