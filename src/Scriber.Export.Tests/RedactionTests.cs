namespace Scriber.Export.Tests;

using Scriber.Core.Domain;
using Scriber.Export.Redaction;
using Xunit;

public class RedactionTests
{
    private readonly RedactionEngine _engine = new();

    [Fact]
    public void ScanText_DetectsEmailsPhoneNumbersAndSSN()
    {
        string text = "Contact Alice at alice@example.com or call +1-555-867-5309. Case SSN is 123-45-6789.";
        var candidates = _engine.ScanText(text);

        Assert.Equal(3, candidates.Count);

        var email = Assert.Single(candidates, c => c.Category == PiiCategory.Email);
        Assert.Equal("alice@example.com", email.OriginalText);
        Assert.Equal("[REDACTED: EMAIL]", email.ReplacementText);

        var phone = Assert.Single(candidates, c => c.Category == PiiCategory.PhoneNumber);
        Assert.Contains("555-867-5309", phone.OriginalText);

        var ssn = Assert.Single(candidates, c => c.Category == PiiCategory.SocialSecurityNumber);
        Assert.Equal("123-45-6789", ssn.OriginalText);
    }

    [Fact]
    public void ScanText_CustomTerms_DetectedAndFlagged()
    {
        string text = "Project Apollo had a confidential briefing regarding AcmeCorp.";
        var options = new RedactionOptions
        {
            CustomTerms = ["Project Apollo", "AcmeCorp"]
        };

        var candidates = _engine.ScanText(text, options);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("Project Apollo", candidates[0].OriginalText);
        Assert.Equal(PiiCategory.CustomTerm, candidates[0].Category);
        Assert.Equal("AcmeCorp", candidates[1].OriginalText);
    }

    [Fact]
    public void Apply_HonorsUserReview_OnlyRedactsAcceptedCandidates()
    {
        string text = "Send details to support@acme.com or call 555-123-4567.";
        var candidates = _engine.ScanText(text);

        Assert.Equal(2, candidates.Count);

        // User reviews: accepts email redaction, but unchecks/rejects phone redaction
        candidates[0].IsAccepted = true; // Email
        candidates[1].IsAccepted = false; // Phone

        string result = _engine.Apply(text, candidates);

        Assert.Contains("[REDACTED: EMAIL]", result);
        Assert.DoesNotContain("support@acme.com", result);
        Assert.Contains("555-123-4567", result); // Preserved!
    }

    [Fact]
    public void RedactTranscript_LeavesOriginalUntouchedAndReturnsRedactedTranscript()
    {
        var audio = new AudioReference("test.wav", TimeSpan.FromMinutes(1), 16000);
        var speaker = new Speaker("spk_0", "Agent Smith", "#10B981");
        var words = new List<Word>
        {
            new("Please", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.4), 0.99f),
            new("email", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.8), 0.99f),
            new("john@doe.com", TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(1.5), 0.99f),
            new("today.", TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(2.0), 0.99f)
        };
        var segment = new Segment("spk_0", TimeSpan.Zero, TimeSpan.FromSeconds(2.1), words);
        var original = new Transcript(Guid.NewGuid(), "Meeting", DateTimeOffset.UtcNow, "en", [speaker], [segment], audio);

        var candidates = _engine.ScanTranscript(original);
        Assert.Single(candidates);
        Assert.Equal("john@doe.com", candidates[0].OriginalText);

        var redacted = _engine.RedactTranscript(original, candidates);

        // Verify original is completely untouched
        Assert.Equal("john@doe.com", original.Segments[0].Words[2].Text);

        // Verify redacted contains placeholder
        string redactedText = string.Join(" ", redacted.Segments[0].Words.Select(w => w.Text));
        Assert.DoesNotContain("john@doe.com", redactedText);
        Assert.Contains("[REDACTED: EMAIL]", redactedText);
    }
}
