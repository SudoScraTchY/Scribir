namespace Scriber.Providers;

public interface ICompletionProvider
{
    /// <summary>
    /// Indicates whether a valid provider endpoint is configured.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends a completion prompt to the provider and returns the generated text.
    /// </summary>
    Task<string> CompleteAsync(string prompt, CompletionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies connectivity to the configured endpoint.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed record CompletionOptions
{
    public float Temperature { get; init; } = 0.3f;
    public int? MaxTokens { get; init; }
    public string? SystemPrompt { get; init; }
}

public sealed record ConnectionTestResult(bool Success, string Message, int? StatusCode = null);

public class ProviderException : Exception
{
    public int? StatusCode { get; }

    public ProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
