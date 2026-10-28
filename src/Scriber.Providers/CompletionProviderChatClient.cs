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

        string promptText = userPromptBuilder.Length > 0
            ? userPromptBuilder.ToString()
            : (systemPrompt ?? string.Empty);

        if (string.IsNullOrWhiteSpace(promptText))
        {
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty))
            {
                ModelId = _modelId
            };
        }

        var completionOpts = new CompletionOptions
        {
            SystemPrompt = userPromptBuilder.Length > 0 ? systemPrompt : null,
            Temperature = (float)(options?.Temperature ?? 0.7f),
            MaxTokens = options?.MaxOutputTokens
        };

        string replyText = await _provider.CompleteAsync(promptText, completionOpts, cancellationToken);
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

        string promptText = userPromptBuilder.Length > 0
            ? userPromptBuilder.ToString()
            : (systemPrompt ?? string.Empty);

        if (string.IsNullOrWhiteSpace(promptText))
        {
            yield break;
        }

        var completionOpts = new CompletionOptions
        {
            SystemPrompt = userPromptBuilder.Length > 0 ? systemPrompt : null,
            Temperature = (float)(options?.Temperature ?? 0.7f),
            MaxTokens = options?.MaxOutputTokens
        };

        await foreach (string delta in _provider.CompleteStreamingAsync(promptText, completionOpts, cancellationToken).ConfigureAwait(false))
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(delta)],
                ModelId = _modelId
            };
        }
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
