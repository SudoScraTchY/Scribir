namespace Scriber.Providers;

using Microsoft.Extensions.AI;

public sealed class CompletionProviderChatClient : IChatClient
{
    private readonly ICompletionProvider _provider;
    private readonly string? _modelId;

    public CompletionProviderChatClient(ICompletionProvider provider, string? modelId = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _modelId = modelId;
    }

    public ChatClientMetadata Metadata => new("Scriber.CompletionProvider", null, _modelId);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatMessages);

        string? systemPrompt = null;
        var userPromptBuilder = new System.Text.StringBuilder();

        foreach (var msg in chatMessages)
        {
            if (msg.Role == ChatRole.System && systemPrompt == null)
            {
                systemPrompt = msg.Text;
            }
            else
            {
                if (userPromptBuilder.Length > 0) userPromptBuilder.AppendLine();
                userPromptBuilder.Append(msg.Text);
            }
        }

        var completionOpts = new CompletionOptions
        {
            SystemPrompt = systemPrompt,
            Temperature = (float)(options?.Temperature ?? 0.7f),
            MaxTokens = options?.MaxOutputTokens
        };

        string replyText = await _provider.CompleteAsync(userPromptBuilder.ToString(), completionOpts, cancellationToken);
        var responseMessage = new ChatMessage(ChatRole.Assistant, replyText);
        return new ChatResponse(responseMessage)
        {
            ModelId = _modelId
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        string text = response.Messages.Count > 0 ? response.Messages[0].Text ?? string.Empty : string.Empty;
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)],
            ModelId = _modelId
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ICompletionProvider))
        {
            return _provider;
        }
        return null;
    }

    public void Dispose()
    {
        if (_provider is IDisposable d)
        {
            d.Dispose();
        }
    }
}
