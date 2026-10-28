namespace Scriber.Providers.Tests;

using System.Net;
using System.Text;
using Scriber.Providers;
using Xunit;

public class OpenAiCompatibleProviderTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Theory]
    [InlineData("http://localhost:11434", "http://localhost:11434/v1/chat/completions")]
    [InlineData("http://localhost:11434/", "http://localhost:11434/v1/chat/completions")]
    [InlineData("http://192.168.1.50:11434/v1", "http://192.168.1.50:11434/v1/chat/completions")]
    [InlineData("http://192.168.1.50:11434/v1/", "http://192.168.1.50:11434/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/chat/completions", "https://api.openai.com/v1/chat/completions")]
    public void ResolveChatCompletionsUrl_ResolvesCorrectly(string input, string expected)
    {
        string resolved = OpenAiCompatibleProvider.ResolveChatCompletionsUrl(input);
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public async Task SettingsStore_PersistsAndLoadsCorrectly()
    {
        var store = new InMemoryProviderSettingsStore();
        var initial = await store.GetSettingsAsync();
        Assert.False(initial.IsConfigured);

        var configured = new ProviderSettings
        {
            BaseUrl = "http://192.168.1.100:11434/v1",
            ApiKey = "test-key-123",
            ModelName = "llama3:8b",
            TimeoutSeconds = 45
        };

        await store.SaveSettingsAsync(configured);
        var loaded = await store.GetSettingsAsync();

        Assert.True(loaded.IsConfigured);
        Assert.Equal("http://192.168.1.100:11434/v1", loaded.BaseUrl);
        Assert.Equal("test-key-123", loaded.ApiKey);
        Assert.Equal("llama3:8b", loaded.ModelName);
        Assert.Equal(45, loaded.TimeoutSeconds);
    }

    [Fact]
    public async Task CompleteAsync_WhenUnconfigured_ThrowsProviderException()
    {
        var provider = new OpenAiCompatibleProvider(new ProviderSettings());
        Assert.False(provider.IsConfigured);

        await Assert.ThrowsAsync<ProviderException>(() => provider.CompleteAsync("Hello"));
    }

    [Fact]
    public async Task CompleteAsync_Success_ExtractsContentAndOmitsAuthHeaderWhenKeyMissing()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            string responseJson = """
            {
                "id": "chatcmpl-123",
                "object": "chat.completion",
                "choices": [
                    {
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": "Meeting concluded with agreed deadlines."
                        },
                        "finish_reason": "stop"
                    }
                ]
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var settings = new ProviderSettings
        {
            BaseUrl = "http://localhost:11434/v1",
            ModelName = "mistral",
            ApiKey = null // LAN self-hosted: no API key
        };

        var httpClient = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(settings, httpClient);

        string result = await provider.CompleteAsync("Summarize please");

        Assert.Equal("Meeting concluded with agreed deadlines.", result);
        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest!.Headers.Authorization);
        Assert.Equal("http://localhost:11434/v1/chat/completions", capturedRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task CompleteAsync_WithApiKey_SetsBearerHeader()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            string responseJson = """
            {
                "choices": [
                    {
                        "message": {
                            "content": "Summary text here."
                        }
                    }
                ]
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var settings = new ProviderSettings
        {
            BaseUrl = "https://api.openai.com/v1",
            ModelName = "gpt-4o-mini",
            ApiKey = "sk-live-secret-999"
        };

        var httpClient = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(settings, httpClient);

        string result = await provider.CompleteAsync("Summarize");

        Assert.Equal("Summary text here.", result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("sk-live-secret-999", capturedRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task CompleteAsync_OnError_ThrowsDescriptiveProviderException()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            string errorJson = """
            {
                "error": {
                    "message": "Model 'nonexistent-model' not found",
                    "type": "invalid_request_error"
                }
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
            };
        });

        var settings = new ProviderSettings
        {
            BaseUrl = "http://localhost:11434/v1",
            ModelName = "nonexistent-model"
        };

        var httpClient = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(settings, httpClient);

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.CompleteAsync("Test prompt"));
        Assert.Equal(404, ex.StatusCode);
        Assert.Contains("Model 'nonexistent-model' not found", ex.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_OnSuccess_ReturnsSuccessResult()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            string responseJson = """
            {
                "choices": [{ "message": { "content": "pong" } }]
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var settings = new ProviderSettings { BaseUrl = "http://localhost:11434/v1" };
        var provider = new OpenAiCompatibleProvider(settings, new HttpClient(handler));

        var result = await provider.TestConnectionAsync();
        Assert.True(result.Success);
        Assert.Contains("Successfully connected", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenUnconfigured_ReturnsFalse()
    {
        var provider = new OpenAiCompatibleProvider(new ProviderSettings());
        var result = await provider.TestConnectionAsync();
        Assert.False(result.Success);
    }

    [Fact]
    public void TranscriptSummarizer_ParsesStructuredOutputCorrectly()
    {
        string rawLlmOutput = """
        # Executive Summary
        The team held a sprint planning meeting to review Phase 2 deliverables and finalize the integration roadmap.

        ## Key Takeaways
        - Phase 1 transcription and storage are 100% verified.
        - Phase 2 focuses on BYOK providers and local MCP server capabilities.
        - Privacy remains strictly on-device without telemetry.

        ## Action Items
        - [Alice] Finalize the MCP server tools and schema.
        - [Bob] Test Xiaomi Redmi Note 11 deployment with adb.
        - Verify all unit test fixtures pass deterministically.
        """;

        var parsed = TranscriptSummarizer.ParseSummary(rawLlmOutput);

        Assert.Contains("The team held a sprint planning meeting", parsed.Summary);
        Assert.Equal(3, parsed.KeyPoints.Count);
        Assert.Equal("Phase 1 transcription and storage are 100% verified.", parsed.KeyPoints[0]);
        Assert.Equal(3, parsed.ActionItems.Count);
        Assert.Equal("Alice", parsed.ActionItems[0].Assignee);
        Assert.Equal("Finalize the MCP server tools and schema.", parsed.ActionItems[0].Description);
        Assert.Equal("Bob", parsed.ActionItems[1].Assignee);
        Assert.Equal("Test Xiaomi Redmi Note 11 deployment with adb.", parsed.ActionItems[1].Description);
        Assert.Null(parsed.ActionItems[2].Assignee);
        Assert.Equal("Verify all unit test fixtures pass deterministically.", parsed.ActionItems[2].Description);
    }

    [Fact]
    public async Task CompleteStreamingAsync_ParsesServerSentEventsAndStreamsDeltas()
    {
        string ssePayload = 
            "data: {\"choices\":[{\"delta\":{\"content\":\"Real-time\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" token\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" streaming.\"}}]}\n\n" +
            "data: [DONE]\n\n";

        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            };
        });

        var settings = new ProviderSettings
        {
            BaseUrl = "http://localhost:11434/v1",
            ModelName = "test-model"
        };

        var provider = new OpenAiCompatibleProvider(settings, new HttpClient(handler));

        var chunks = new List<string>();
        await foreach (var chunk in provider.CompleteStreamingAsync("Hello"))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Real-time", chunks[0]);
        Assert.Equal(" token", chunks[1]);
        Assert.Equal(" streaming.", chunks[2]);
        Assert.Equal("Real-time token streaming.", string.Concat(chunks));
    }
}
