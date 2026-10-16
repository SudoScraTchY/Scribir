namespace Scriber.Export.Redaction;

public sealed class RedactionCandidate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string OriginalText { get; init; }
    public int StartIndex { get; init; }
    public int Length { get; init; }
    public PiiCategory Category { get; init; }
    public string ReplacementText { get; set; }
    public bool IsAccepted { get; set; } = true;
    public string Context { get; init; }

    public RedactionCandidate(
        string originalText,
        int startIndex,
        int length,
        PiiCategory category,
        string replacementText,
        string context,
        bool isAccepted = true)
    {
        OriginalText = originalText ?? throw new ArgumentNullException(nameof(originalText));
        StartIndex = startIndex;
        Length = length;
        Category = category;
        ReplacementText = replacementText ?? throw new ArgumentNullException(nameof(replacementText));
        Context = context ?? string.Empty;
        IsAccepted = isAccepted;
    }
}
