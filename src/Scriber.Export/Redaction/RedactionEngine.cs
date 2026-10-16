namespace Scriber.Export.Redaction;

using System.Text.RegularExpressions;
using Scriber.Core.Domain;

public sealed class RedactionEngine : IRedactionEngine
{
    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PhoneRegex = new(
        @"(?:(?:\+?\d{1,3}[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b)|(?:\b(?:0|\+98)?9\d{9}\b)",
        RegexOptions.Compiled);

    private static readonly Regex SsnRegex = new(
        @"\b\d{3}-\d{2}-\d{4}\b",
        RegexOptions.Compiled);

    private static readonly Regex CreditCardCandidateRegex = new(
        @"\b(?:\d[ -]*?){13,19}\b",
        RegexOptions.Compiled);

    public IReadOnlyList<RedactionCandidate> ScanText(string text, RedactionOptions? options = null)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<RedactionCandidate>();
        options ??= new RedactionOptions();

        var candidates = new List<RedactionCandidate>();

        // 1. Emails
        if (options.RedactEmails)
        {
            foreach (Match m in EmailRegex.Matches(text))
            {
                candidates.Add(new RedactionCandidate(
                    m.Value,
                    m.Index,
                    m.Length,
                    PiiCategory.Email,
                    options.EmailPlaceholder,
                    ExtractContext(text, m.Index, m.Length)));
            }
        }

        // 2. SSN
        if (options.RedactSocialSecurityNumbers)
        {
            foreach (Match m in SsnRegex.Matches(text))
            {
                candidates.Add(new RedactionCandidate(
                    m.Value,
                    m.Index,
                    m.Length,
                    PiiCategory.SocialSecurityNumber,
                    options.SsnPlaceholder,
                    ExtractContext(text, m.Index, m.Length)));
            }
        }

        // 3. Credit Cards (with Luhn check)
        if (options.RedactCreditCards)
        {
            foreach (Match m in CreditCardCandidateRegex.Matches(text))
            {
                string digits = Regex.Replace(m.Value, @"\D", "");
                if (digits.Length >= 13 && digits.Length <= 19 && IsValidLuhn(digits))
                {
                    candidates.Add(new RedactionCandidate(
                        m.Value,
                        m.Index,
                        m.Length,
                        PiiCategory.CreditCard,
                        options.CreditCardPlaceholder,
                        ExtractContext(text, m.Index, m.Length)));
                }
            }
        }

        // 4. Phone Numbers (avoiding overlapping with SSN/Credit cards)
        if (options.RedactPhoneNumbers)
        {
            foreach (Match m in PhoneRegex.Matches(text))
            {
                bool overlaps = candidates.Any(c => m.Index < c.StartIndex + c.Length && c.StartIndex < m.Index + m.Length);
                if (!overlaps)
                {
                    candidates.Add(new RedactionCandidate(
                        m.Value,
                        m.Index,
                        m.Length,
                        PiiCategory.PhoneNumber,
                        options.PhonePlaceholder,
                        ExtractContext(text, m.Index, m.Length)));
                }
            }
        }

        // 5. Custom Terms
        if (options.CustomTerms != null && options.CustomTerms.Count > 0)
        {
            foreach (var term in options.CustomTerms)
            {
                if (string.IsNullOrWhiteSpace(term)) continue;

                string pattern = $@"\b{Regex.Escape(term.Trim())}\b";
                foreach (Match m in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
                {
                    bool overlaps = candidates.Any(c => m.Index < c.StartIndex + c.Length && c.StartIndex < m.Index + m.Length);
                    if (!overlaps)
                    {
                        candidates.Add(new RedactionCandidate(
                            m.Value,
                            m.Index,
                            m.Length,
                            PiiCategory.CustomTerm,
                            options.CustomTermPlaceholder,
                            ExtractContext(text, m.Index, m.Length)));
                    }
                }
            }
        }

        return candidates.OrderBy(c => c.StartIndex).ToList();
    }

    public IReadOnlyList<RedactionCandidate> ScanTranscript(Transcript transcript, RedactionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        options ??= new RedactionOptions();

        var allCandidates = new List<RedactionCandidate>();

        foreach (var segment in transcript.Segments)
        {
            string segmentText = string.Join(" ", segment.Words.Select(w => w.Text));
            var candidates = ScanText(segmentText, options);
            allCandidates.AddRange(candidates);
        }

        return allCandidates;
    }

    public string Apply(string text, IEnumerable<RedactionCandidate> candidates)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        ArgumentNullException.ThrowIfNull(candidates);

        var accepted = candidates
            .Where(c => c.IsAccepted)
            .OrderByDescending(c => c.StartIndex)
            .ToList();

        if (accepted.Count == 0) return text;

        var sb = new System.Text.StringBuilder(text);
        foreach (var c in accepted)
        {
            if (c.StartIndex >= 0 && c.StartIndex + c.Length <= sb.Length)
            {
                sb.Remove(c.StartIndex, c.Length);
                sb.Insert(c.StartIndex, c.ReplacementText);
            }
        }

        return sb.ToString();
    }

    public Transcript RedactTranscript(Transcript transcript, IEnumerable<RedactionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(candidates);

        var acceptedList = candidates.Where(c => c.IsAccepted).ToList();
        if (acceptedList.Count == 0) return transcript;

        var newSegments = new List<Segment>();

        foreach (var segment in transcript.Segments)
        {
            string segmentText = string.Join(" ", segment.Words.Select(w => w.Text));
            var segCandidates = acceptedList
                .Where(c => segmentText.Contains(c.OriginalText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (segCandidates.Count == 0)
            {
                newSegments.Add(segment);
                continue;
            }

            string redactedText = Apply(segmentText, segCandidates);
            var words = redactedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var newWords = new List<Word>();
            TimeSpan duration = segment.End - segment.Start;
            double step = words.Length > 0 ? duration.TotalSeconds / words.Length : 0;

            for (int i = 0; i < words.Length; i++)
            {
                TimeSpan wordStart = segment.Start + TimeSpan.FromSeconds(i * step);
                TimeSpan wordEnd = i == words.Length - 1 ? segment.End : wordStart + TimeSpan.FromSeconds(step * 0.9);
                newWords.Add(new Word(words[i], wordStart, wordEnd, 1.0f));
            }

            newSegments.Add(new Segment(segment.SpeakerId, segment.Start, segment.End, newWords));
        }

        return new Transcript(
            transcript.Id,
            transcript.Title,
            transcript.CreatedAt,
            transcript.LanguageCode,
            transcript.Speakers,
            newSegments,
            transcript.Audio);
    }

    private static string ExtractContext(string text, int index, int length)
    {
        int start = Math.Max(0, index - 25);
        int end = Math.Min(text.Length, index + length + 25);
        string snippet = text.Substring(start, end - start).Trim();
        return (start > 0 ? "..." : "") + snippet + (end < text.Length ? "..." : "");
    }

    private static bool IsValidLuhn(string digits)
    {
        int sum = 0;
        bool alternate = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9) n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }
}
