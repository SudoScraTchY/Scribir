namespace Scriber.Providers;

using Microsoft.Extensions.AI;

/// <summary>
/// Extension methods for bridging Scribir <see cref="ICompletionProvider"/> implementations
/// into Microsoft Agent Framework and Microsoft.Extensions.AI <see cref="IChatClient"/>.
/// </summary>
public static class CompletionProviderExtensions
{
    /// <summary>
    /// Wraps an <see cref="ICompletionProvider"/> into an <see cref="IChatClient"/> instance.
    /// </summary>
    public static IChatClient AsChatClient(this ICompletionProvider provider, string? modelId = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new CompletionProviderChatClient(provider, modelId);
    }
}
