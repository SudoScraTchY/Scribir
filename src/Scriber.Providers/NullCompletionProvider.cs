namespace Scriber.Providers;

public sealed class NullCompletionProvider : ICompletionProvider
{
    public static readonly NullCompletionProvider Instance = new();

    public bool IsConfigured => false;

    public Task<string> CompleteAsync(string prompt, CompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("No AI completion provider is configured. Please configure an OpenAI-compatible endpoint or model in Settings.");
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ConnectionTestResult(false, "No AI provider is configured."));
    }
}
