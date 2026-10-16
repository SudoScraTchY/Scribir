namespace Scriber.Providers;

using System.Text.RegularExpressions;

public sealed class TranscriptSummarizer : ISummarizer
{
    private readonly ICompletionProvider _provider;

    public const string DefaultSystemPrompt = 
        "You are an expert meeting assistant. Given an audio transcript with speaker attributions, provide:\n" +
        "1. Executive Summary (a concise 2-3 sentence overview)\n" +
        "2. Key Takeaways (bulleted list of key discussion points)\n" +
        "3. Action Items (bulleted list with speaker/assignee if mentioned, in format: - [Speaker] Task)";

    public TranscriptSummarizer(ICompletionProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<TranscriptSummaryResult> SummarizeAsync(string transcriptText, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptText);

        if (!_provider.IsConfigured)
        {
            throw new ProviderException("Summarization provider is not configured. Please set an OpenAI-compatible endpoint in Settings.");
        }

        string userPrompt = $"Please summarize the following transcript:\n\n{transcriptText}";

        var options = new CompletionOptions
        {
            SystemPrompt = DefaultSystemPrompt,
            Temperature = 0.2f
        };

        string completion = await _provider.CompleteAsync(userPrompt, options, cancellationToken);
        return ParseSummary(completion);
    }

    public static TranscriptSummaryResult ParseSummary(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return new TranscriptSummaryResult(string.Empty, Array.Empty<string>(), Array.Empty<ActionItem>());
        }

        var lines = rawResponse.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var summaryLines = new List<string>();
        var keyPoints = new List<string>();
        var actionItems = new List<ActionItem>();

        int section = 0; // 0 = summary, 1 = key points, 2 = action items

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string lower = trimmed.ToLowerInvariant();
            if (lower.Contains("executive summary") || lower.StartsWith("# summary") || lower.StartsWith("## summary"))
            {
                section = 0;
                continue;
            }
            if (lower.Contains("key takeaway") || lower.Contains("key point") || lower.Contains("discussion point"))
            {
                section = 1;
                continue;
            }
            if (lower.Contains("action item") || lower.Contains("next step") || lower.Contains("tasks"))
            {
                section = 2;
                continue;
            }

            if (section == 0)
            {
                summaryLines.Add(trimmed.TrimStart('#', '*', ' '));
            }
            else if (section == 1)
            {
                string clean = trimmed.TrimStart('-', '*', '•', ' ', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.');
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    keyPoints.Add(clean.Trim());
                }
            }
            else if (section == 2)
            {
                string clean = trimmed.TrimStart('-', '*', '•', ' ', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.');
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    // Check for [Speaker] or (Speaker) pattern
                    var match = Regex.Match(clean, @"^\[(.*?)\]\s*(.*)$");
                    if (match.Success)
                    {
                        actionItems.Add(new ActionItem(match.Groups[2].Value.Trim(), match.Groups[1].Value.Trim()));
                    }
                    else
                    {
                        actionItems.Add(new ActionItem(clean.Trim()));
                    }
                }
            }
        }

        string fullSummary = summaryLines.Count > 0 ? string.Join(" ", summaryLines) : rawResponse.Trim();
        return new TranscriptSummaryResult(fullSummary, keyPoints, actionItems);
    }
}
