namespace Scriber.Providers;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class OpenAiCompatibleProvider : ICompletionProvider
{
    private readonly HttpClient _httpClient;
    private readonly IProviderSettingsStore? _settingsStore;
    private ProviderSettings? _staticSettings;

    public OpenAiCompatibleProvider(IProviderSettingsStore settingsStore, HttpClient? httpClient = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _httpClient = httpClient ?? new HttpClient();
    }

    public OpenAiCompatibleProvider(ProviderSettings settings, HttpClient? httpClient = null)
    {
        _staticSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? new HttpClient();
    }

    public bool IsConfigured
    {
        get
        {
            var settings = CurrentSettings;
            return settings.IsConfigured;
        }
    }

    private ProviderSettings CurrentSettings
    {
        get
        {
            if (_staticSettings != null) return _staticSettings;
            if (_settingsStore != null)
            {
                // Synchronous peek for property; async preferred for operations
                return _settingsStore.GetSettingsAsync().GetAwaiter().GetResult();
            }
            return ProviderSettings.Empty;
        }
    }

    private async Task<ProviderSettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        if (_staticSettings != null) return _staticSettings;
        if (_settingsStore != null) return await _settingsStore.GetSettingsAsync(cancellationToken);
        return ProviderSettings.Empty;
    }

    public async Task<string> CompleteAsync(string prompt, CompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var settings = await GetSettingsAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            throw new ProviderException("OpenAI-compatible provider is not configured. Base URL is missing.");
        }

        string endpoint = ResolveChatCompletionsUrl(settings.BaseUrl!);
        string model = string.IsNullOrWhiteSpace(settings.ModelName) ? "default" : settings.ModelName;

        options ??= new CompletionOptions();

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
        {
            messages.Add(new { role = "system", content = options.SystemPrompt });
        }
        messages.Add(new { role = "user", content = prompt });

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = options.Temperature,
            ["stream"] = false
        };

        if (options.MaxTokens.HasValue)
        {
            requestBody["max_tokens"] = options.MaxTokens.Value;
        }

        string jsonPayload = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 60));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException($"Request to '{endpoint}' timed out after {settings.TimeoutSeconds}s.");
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException($"Failed to communicate with provider at '{endpoint}': {ex.Message}", innerException: ex);
        }

        string responseJson = await response.Content.ReadAsStringAsync(linkedCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            string errorMessage = ExtractErrorMessage(responseJson) ?? response.ReasonPhrase ?? "HTTP " + (int)response.StatusCode;
            throw new ProviderException($"Provider returned error {(int)response.StatusCode}: {errorMessage}", (int)response.StatusCode);
        }

        return ExtractContent(responseJson);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            return new ConnectionTestResult(false, "Base URL is not configured.");
        }

        try
        {
            // Execute a lightweight 1-token prompt to test end-to-end endpoint responsiveness
            var testOptions = new CompletionOptions
            {
                MaxTokens = 1,
                Temperature = 0.0f
            };

            await CompleteAsync("ping", testOptions, cancellationToken);
            return new ConnectionTestResult(true, "Successfully connected to endpoint.");
        }
        catch (ProviderException ex)
        {
            return new ConnectionTestResult(false, ex.Message, ex.StatusCode);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"Connection error: {ex.Message}");
        }
    }

    public static string ResolveChatCompletionsUrl(string baseUrl)
    {
        string trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{trimmed}/chat/completions";
        }

        return $"{trimmed}/v1/chat/completions";
    }

    private static string? ExtractErrorMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var errorNode = node?["error"];
            if (errorNode != null)
            {
                var msg = errorNode["message"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(msg)) return msg;
                return errorNode.ToJsonString();
            }
        }
        catch
        {
            // Ignore parse errors on error payload
        }
        return null;
    }

    private static string ExtractContent(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var choices = node?["choices"]?.AsArray();
            if (choices != null && choices.Count > 0)
            {
                var content = choices[0]?["message"]?["content"]?.GetValue<string>();
                if (content != null) return content.Trim();
            }

            throw new ProviderException("Provider returned response without valid choices content: " + json);
        }
        catch (Exception ex) when (ex is not ProviderException)
        {
            throw new ProviderException("Failed to parse provider response JSON: " + ex.Message, innerException: ex);
        }
    }
}
