namespace Scriber.Export.Redaction;

public sealed record RedactionOptions
{
    public bool RedactEmails { get; init; } = true;
    public bool RedactPhoneNumbers { get; init; } = true;
    public bool RedactCreditCards { get; init; } = true;
    public bool RedactSocialSecurityNumbers { get; init; } = true;
    public IReadOnlyList<string> CustomTerms { get; init; } = [];

    public string EmailPlaceholder { get; init; } = "[REDACTED: EMAIL]";
    public string PhonePlaceholder { get; init; } = "[REDACTED: PHONE]";
    public string CreditCardPlaceholder { get; init; } = "[REDACTED: CARD]";
    public string SsnPlaceholder { get; init; } = "[REDACTED: SSN]";
    public string CustomTermPlaceholder { get; init; } = "[REDACTED]";
}
