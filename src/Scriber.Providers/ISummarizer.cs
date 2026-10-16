namespace Scriber.Providers;

public interface ISummarizer
{
    Task<TranscriptSummaryResult> SummarizeAsync(string transcriptText, CancellationToken cancellationToken = default);
}

public sealed record TranscriptSummaryResult(
    string Summary,
    IReadOnlyList<string> KeyPoints,
    IReadOnlyList<ActionItem> ActionItems
);

public sealed record ActionItem(string Description, string? Assignee = null);
