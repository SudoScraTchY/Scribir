namespace Scriber.Providers.Tests;

using Microsoft.Extensions.AI;
using Scriber.Providers;
using Xunit;

public class CompletionProviderChatClientTests
{
    private sealed class EchoCompletionProvider : ICompletionProvider
    {
        public bool IsConfigured => true;
        public string? LastPrompt { get; private set; }
        public CompletionOptions? LastOptions { get; private set; }

        public Task<string> CompleteAsync(string prompt, CompletionOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            LastOptions = options;
            return Task.FromResult($"ECHO: {prompt}");
        }

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ConnectionTestResult(true, "Connected successfully"));
        }
    }

    [Fact]
    public async Task GetResponseAsync_TranslatesMessagesAndOptionsCorrectly()
    {
        var echo = new EchoCompletionProvider();
        IChatClient client = echo.AsChatClient(modelId: "test-model-llama");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful meeting summarizer."),
            new(ChatRole.User, "Summarize the key decisions.")
        };

        var options = new ChatOptions
        {
            Temperature = 0.4f,
            MaxOutputTokens = 500
        };

        var response = await client.GetResponseAsync(messages, options);

        Assert.NotNull(response);
        Assert.Single(response.Messages);
        Assert.Equal("ECHO: Summarize the key decisions.", response.Messages[0].Text);
        Assert.Equal("test-model-llama", response.ModelId);

        Assert.Equal("Summarize the key decisions.", echo.LastPrompt);
        Assert.NotNull(echo.LastOptions);
        Assert.Equal("You are a helpful meeting summarizer.", echo.LastOptions.SystemPrompt);
        Assert.Equal(0.4f, echo.LastOptions.Temperature, 0.01f);
        Assert.Equal(500, echo.LastOptions.MaxTokens);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_YieldsAssistantResponseUpdate()
    {
        var echo = new EchoCompletionProvider();
        var client = new CompletionProviderChatClient(echo, "local-qwen");

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Live test")]))
        {
            updates.Add(update);
        }

        Assert.Single(updates);
        Assert.Equal(ChatRole.Assistant, updates[0].Role);
        Assert.Equal("ECHO: Live test", updates[0].Text);
        Assert.Equal("local-qwen", updates[0].ModelId);
    }

    [Fact]
    public void GetService_ReturnsUnderlyingProvider()
    {
        var echo = new EchoCompletionProvider();
        using var client = echo.AsChatClient();

        var retrieved = client.GetService<ICompletionProvider>();
        Assert.Same(echo, retrieved);
    }
}
