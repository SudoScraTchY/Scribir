namespace Scriber.Mcp.Tests;

using System.Text.Json.Nodes;
using Scriber.Core.Domain;
using Scriber.Mcp;
using Scriber.Providers;
using Scriber.Storage;
using Xunit;

public class McpServerTests
{
    private sealed class MockVoiceprintRepository : IVoiceprintRepository
    {
        private readonly List<Voiceprint> _voiceprints = new();

        public MockVoiceprintRepository(params Voiceprint[] voiceprints)
        {
            _voiceprints.AddRange(voiceprints);
        }

        public Task<IReadOnlyList<Voiceprint>> GetAllAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<Voiceprint>>(_voiceprints.ToList());
        }

        public Task<Voiceprint?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return Task.FromResult(_voiceprints.FirstOrDefault(v => v.Id == id));
        }

        public Task SaveAsync(Voiceprint voiceprint, CancellationToken ct = default)
        {
            _voiceprints.RemoveAll(v => v.Id == voiceprint.Id);
            _voiceprints.Add(voiceprint);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _voiceprints.RemoveAll(v => v.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class MockSummarizer : ISummarizer
    {
        private readonly TranscriptSummaryResult _result;

        public MockSummarizer(TranscriptSummaryResult result)
        {
            _result = result;
        }

        public Task<TranscriptSummaryResult> SummarizeAsync(string transcriptText, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
    private sealed class MockTranscriptRepository : ITranscriptRepository
    {
        private readonly List<Transcript> _transcripts = new();

        public MockTranscriptRepository(params Transcript[] transcripts)
        {
            _transcripts.AddRange(transcripts);
        }

        public Task<Transcript?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            return Task.FromResult(_transcripts.FirstOrDefault(t => t.Id == id));
        }

        public Task<IReadOnlyList<TranscriptSummary>> ListAsync(CancellationToken ct = default)
        {
            var summaries = _transcripts.Select(t => new TranscriptSummary(
                t.Id,
                t.Title,
                t.CreatedAt,
                t.LanguageCode,
                t.Audio.Duration,
                t.Speakers.Count,
                t.Segments.Count
            )).ToList();

            return Task.FromResult<IReadOnlyList<TranscriptSummary>>(summaries);
        }

        public Task SaveAsync(Transcript transcript, CancellationToken ct = default)
        {
            _transcripts.RemoveAll(t => t.Id == transcript.Id);
            _transcripts.Add(transcript);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _transcripts.RemoveAll(t => t.Id == id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
        {
            var results = new List<SearchResult>();
            foreach (var t in _transcripts)
            {
                foreach (var seg in t.Segments)
                {
                    string text = string.Join(" ", seg.Words.Select(w => w.Text));
                    if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new SearchResult(
                            t.Id,
                            t.Title,
                            1,
                            seg.SpeakerId,
                            seg.Start,
                            seg.End,
                            text,
                            $"Snippet: {text}"
                        ));
                    }
                }
            }
            return Task.FromResult<IReadOnlyList<SearchResult>>(results);
        }

        public Task<Guid> ArchiveVersionAsync(Guid transcriptId, string note = "", CancellationToken ct = default)
        {
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<IReadOnlyList<TranscriptVersionInfo>> GetVersionsAsync(Guid transcriptId, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<TranscriptVersionInfo>>([]);
        }

        public Task<Transcript?> GetVersionAsync(Guid versionId, CancellationToken ct = default)
        {
            return Task.FromResult<Transcript?>(null);
        }
    }

    private static Transcript CreateFixtureTranscript(Guid id, string title, string spokenText)
    {
        var audio = new AudioReference("test.wav", TimeSpan.FromMinutes(2), 16000);
        var speaker = new Speaker("spk_0", "Dr. Jane Smith", "#2563EB");
        var words = spokenText.Split(' ')
            .Select((w, i) => new Word(w, TimeSpan.FromSeconds(i * 0.5), TimeSpan.FromSeconds(i * 0.5 + 0.4), 0.95f))
            .ToList();
        var segment = new Segment("spk_0", TimeSpan.Zero, TimeSpan.FromSeconds(words.Count * 0.5 + 0.1), words);

        return new Transcript(id, title, DateTimeOffset.UtcNow, "en", [speaker], [segment], audio);
    }

    [Fact]
    public async Task Initialize_ReturnsProtocolVersionAndCapabilities()
    {
        var repo = new MockTranscriptRepository();
        var server = new McpServer(repo);

        string request = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}""";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        Assert.Equal("2.0", response!["jsonrpc"]?.GetValue<string>());
        Assert.Equal(1, response["id"]?.GetValue<int>());

        var result = response["result"] as JsonObject;
        Assert.NotNull(result);
        Assert.Equal("scriber-mcp", result!["serverInfo"]?["name"]?.GetValue<string>());
        Assert.NotNull(result["capabilities"]?["tools"]);
        Assert.NotNull(result["capabilities"]?["resources"]);
    }

    [Fact]
    public async Task Ping_ReturnsEmptyResult()
    {
        var repo = new MockTranscriptRepository();
        var server = new McpServer(repo);

        string request = """{"jsonrpc":"2.0","id":42,"method":"ping"}""";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        Assert.Equal(42, response!["id"]?.GetValue<int>());
        Assert.NotNull(response["result"]);
    }

    [Fact]
    public async Task ToolsList_ExposesRequiredTools()
    {
        var repo = new MockTranscriptRepository();
        var server = new McpServer(repo);

        string request = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var tools = response!["result"]?["tools"]?.AsArray();
        Assert.NotNull(tools);

        var toolNames = tools!.Select(t => t?["name"]?.GetValue<string>()).ToList();
        Assert.Contains("list_transcripts", toolNames);
        Assert.Contains("search_transcripts", toolNames);
        Assert.Contains("get_transcript", toolNames);
    }

    [Fact]
    public async Task ToolsCall_ListTranscripts_ReturnsSummaries()
    {
        var id = Guid.NewGuid();
        var transcript = CreateFixtureTranscript(id, "Sprint Review", "We finalized the architectural milestones.");
        var repo = new MockTranscriptRepository(transcript);
        var server = new McpServer(repo);

        string request = """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_transcripts","arguments":{"limit":10}}}""";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var content = response!["result"]?["content"]?.AsArray();
        Assert.NotNull(content);
        string text = content![0]?["text"]?.GetValue<string>() ?? "";

        Assert.Contains("Sprint Review", text);
        Assert.Contains(id.ToString(), text);
    }

    [Fact]
    public async Task ToolsCall_SearchTranscripts_ReturnsMatchingSegments()
    {
        var id = Guid.NewGuid();
        var transcript = CreateFixtureTranscript(id, "Research Discussion", "The quantized onnx model achieves high inference efficiency.");
        var repo = new MockTranscriptRepository(transcript);
        var server = new McpServer(repo);

        string request = """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"search_transcripts","arguments":{"query":"quantized"}}}""";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var content = response!["result"]?["content"]?.AsArray();
        Assert.NotNull(content);
        string text = content![0]?["text"]?.GetValue<string>() ?? "";

        Assert.Contains("quantized", text);
        Assert.Contains("Research Discussion", text);
    }

    [Fact]
    public async Task ToolsCall_GetTranscript_ReturnsMarkdownWithYamlFrontmatter()
    {
        var id = Guid.NewGuid();
        var transcript = CreateFixtureTranscript(id, "Project Kickoff", "Welcome everyone to the session.");
        var repo = new MockTranscriptRepository(transcript);
        var server = new McpServer(repo);

        string request = $"{{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{{\"name\":\"get_transcript\",\"arguments\":{{\"id\":\"{id}\",\"includeTimestamps\":true}}}}}}";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var content = response!["result"]?["content"]?.AsArray();
        Assert.NotNull(content);
        string markdown = content![0]?["text"]?.GetValue<string>() ?? "";

        Assert.StartsWith("---", markdown);
        Assert.Contains("Project Kickoff", markdown);
        Assert.Contains("participants:", markdown);
        Assert.Contains("Dr. Jane Smith", markdown);
        Assert.Contains("Welcome everyone to the session.", markdown);
    }

    [Fact]
    public async Task ResourcesListAndRead_ReturnsValidMarkdownResource()
    {
        var id = Guid.NewGuid();
        var transcript = CreateFixtureTranscript(id, "Executive Sync", "Financial forecasts exceed expectations.");
        var repo = new MockTranscriptRepository(transcript);
        var server = new McpServer(repo);

        // 1. List
        string listReq = """{"jsonrpc":"2.0","id":6,"method":"resources/list"}""";
        var listResp = await server.ProcessMessageAsync(listReq);
        Assert.NotNull(listResp);
        var resources = listResp!["result"]?["resources"]?.AsArray();
        Assert.NotNull(resources);
        Assert.Single(resources!);
        Assert.Equal($"transcript://{id}", resources![0]?["uri"]?.GetValue<string>());
        Assert.Equal("text/markdown", resources[0]?["mimeType"]?.GetValue<string>());

        // 2. Read
        string readReq = $"{{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"resources/read\",\"params\":{{\"uri\":\"transcript://{id}\"}}}}";
        var readResp = await server.ProcessMessageAsync(readReq);
        Assert.NotNull(readResp);
        var contents = readResp!["result"]?["contents"]?.AsArray();
        Assert.NotNull(contents);
        Assert.Single(contents!);
        string markdown = contents![0]?["text"]?.GetValue<string>() ?? "";
        Assert.Contains("Financial forecasts exceed expectations.", markdown);
        Assert.StartsWith("---", markdown);
    }

    [Fact]
    public async Task InvalidMethod_ReturnsMethodNotFoundRpcError()
    {
        var repo = new MockTranscriptRepository();
        var server = new McpServer(repo);

        string request = """{"jsonrpc":"2.0","id":99,"method":"delete_all_transcripts"}""";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var error = response!["error"] as JsonObject;
        Assert.NotNull(error);
        Assert.Equal(-32601, error!["code"]?.GetValue<int>());
    }

    [Fact]
    public async Task ToolsList_IncludesAllEightTools()
    {
        var repo = new MockTranscriptRepository();
        var server = new McpServer(repo);

        string request = """{"jsonrpc":"2.0","id":100,"method":"tools/list"}""";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var tools = response!["result"]?["tools"]?.AsArray();
        Assert.NotNull(tools);
        Assert.Equal(8, tools!.Count);
        var toolNames = tools.Select(t => t?["name"]?.GetValue<string>()).ToList();
        Assert.Contains("list_transcripts", toolNames);
        Assert.Contains("search_transcripts", toolNames);
        Assert.Contains("get_transcript", toolNames);
        Assert.Contains("get_transcript_summary", toolNames);
        Assert.Contains("search_by_voiceprint", toolNames);
        Assert.Contains("inject_custom_vocabulary", toolNames);
        Assert.Contains("get_custom_vocabulary", toolNames);
        Assert.Contains("summarize_audio", toolNames);
    }

    [Fact]
    public async Task ToolsCall_InjectAndGetCustomVocabulary_ManagesTermsCorrectly()
    {
        var repo = new MockTranscriptRepository();
        var server = new McpServer(repo);

        // 1. Inject initial terms
        string injectReq = """
        {
            "jsonrpc": "2.0",
            "id": 105,
            "method": "tools/call",
            "params": {
                "name": "inject_custom_vocabulary",
                "arguments": {
                    "terms": ["Scribir", "Vazirmatn", "Nemotron"]
                }
            }
        }
        """;
        var injectResp = await server.ProcessMessageAsync(injectReq);
        Assert.NotNull(injectResp);
        Assert.False(injectResp!["result"]?["isError"]?.GetValue<bool>());

        // 2. Get terms
        string getReq = """
        {
            "jsonrpc": "2.0",
            "id": 106,
            "method": "tools/call",
            "params": {
                "name": "get_custom_vocabulary",
                "arguments": {}
            }
        }
        """;
        var getResp = await server.ProcessMessageAsync(getReq);
        Assert.NotNull(getResp);
        string text = getResp!["result"]?["content"]?.AsArray()?[0]?["text"]?.GetValue<string>() ?? "";
        Assert.Contains("Scribir", text);
        Assert.Contains("Vazirmatn", text);
        Assert.Contains("Nemotron", text);

        // 3. Inject with clearExisting
        string clearAndInjectReq = """
        {
            "jsonrpc": "2.0",
            "id": 107,
            "method": "tools/call",
            "params": {
                "name": "inject_custom_vocabulary",
                "arguments": {
                    "terms": ["FastSpeech"],
                    "clearExisting": true
                }
            }
        }
        """;
        await server.ProcessMessageAsync(clearAndInjectReq);
        var getResp2 = await server.ProcessMessageAsync(getReq);
        string text2 = getResp2!["result"]?["content"]?.AsArray()?[0]?["text"]?.GetValue<string>() ?? "";
        Assert.Contains("FastSpeech", text2);
        Assert.DoesNotContain("Vazirmatn", text2);
    }

    [Fact]
    public async Task ToolsCall_SummarizeAudio_WithDirectText_ReturnsSummary()
    {
        var repo = new MockTranscriptRepository();
        var server = new McpServer(repo);

        string request = """
        {
            "jsonrpc": "2.0",
            "id": 108,
            "method": "tools/call",
            "params": {
                "name": "summarize_audio",
                "arguments": {
                    "text": "The zero allocation ring buffer eliminated GC pressure. Real token streaming was verified with server sent events.",
                    "focus": "performance"
                }
            }
        }
        """;
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var content = response!["result"]?["content"]?.AsArray();
        Assert.NotNull(content);
        string text = content![0]?["text"]?.GetValue<string>() ?? "";
        Assert.Contains("Summary for audio session", text);
        Assert.Contains("zero allocation ring buffer", text);
    }

    [Fact]
    public async Task ToolsCall_GetTranscriptSummary_WithSummarizer_ReturnsStructuredSummary()
    {
        var id = Guid.NewGuid();
        var transcript = CreateFixtureTranscript(id, "Design Review", "We decided to implement local vector search first.");
        var repo = new MockTranscriptRepository(transcript);
        var expectedSummary = new TranscriptSummaryResult(
            "Discussion on local search implementation.",
            new[] { "Local search prioritized", "ADR-0008 frontmatter enforced" },
            new[] { new ActionItem("Ship vector search", "Jane") }
        );
        var summarizer = new MockSummarizer(expectedSummary);
        var server = new McpServer(repo, null, summarizer);

        string request = $"{{\"jsonrpc\":\"2.0\",\"id\":101,\"method\":\"tools/call\",\"params\":{{\"name\":\"get_transcript_summary\",\"arguments\":{{\"id\":\"{id}\",\"focus\":\"decisions\"}}}}}}";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var content = response!["result"]?["content"]?.AsArray();
        Assert.NotNull(content);
        string text = content![0]?["text"]?.GetValue<string>() ?? "";
        Assert.Contains("Discussion on local search implementation.", text);
        Assert.Contains("Local search prioritized", text);
        Assert.Contains("Ship vector search", text);
        Assert.Contains("Jane", text);
    }

    [Fact]
    public async Task ToolsCall_GetTranscriptSummary_FallbackWithoutSummarizer_ReturnsExtractiveSummary()
    {
        var id = Guid.NewGuid();
        var transcript = CreateFixtureTranscript(id, "Design Review", "This is a sufficiently long sentence that will be picked up as a key point in our fallback extractive summary.");
        var repo = new MockTranscriptRepository(transcript);
        var server = new McpServer(repo);

        string request = $"{{\"jsonrpc\":\"2.0\",\"id\":102,\"method\":\"tools/call\",\"params\":{{\"name\":\"get_transcript_summary\",\"arguments\":{{\"id\":\"{id}\"}}}}}}";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var content = response!["result"]?["content"]?.AsArray();
        Assert.NotNull(content);
        string text = content![0]?["text"]?.GetValue<string>() ?? "";
        Assert.Contains("Design Review", text);
        Assert.Contains("keyPoints", text);
    }

    [Fact]
    public async Task ToolsCall_SearchByVoiceprint_MatchesTranscriptAndSimilarVoiceprints()
    {
        var vpId1 = Guid.NewGuid();
        var vpId2 = Guid.NewGuid();
        var vp1 = new Voiceprint(vpId1, "Alice", new float[] { 1f, 0f, 0f, 0f }, DateTimeOffset.UtcNow);
        var vp2 = new Voiceprint(vpId2, "Alice (Duplicate)", new float[] { 0.99f, 0.05f, 0f, 0f }, DateTimeOffset.UtcNow);

        var vpRepo = new MockVoiceprintRepository(vp1, vp2);

        var audio = new AudioReference("meeting.wav", TimeSpan.FromMinutes(5), 16000);
        var speakerAlice = new Speaker("spk_alice", "Alice Primary", "#10B981", voiceprintId: vpId1);
        var seg = new Segment("spk_alice", TimeSpan.Zero, TimeSpan.FromSeconds(3), new[]
        {
            new Word("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.98f),
            new Word("everyone", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0.97f)
        });
        var transcript = new Transcript(Guid.NewGuid(), "Morning Standup", DateTimeOffset.UtcNow, "en", new[] { speakerAlice }, new[] { seg }, audio);
        var transcriptRepo = new MockTranscriptRepository(transcript);

        var server = new McpServer(transcriptRepo, vpRepo);

        string request = $"{{\"jsonrpc\":\"2.0\",\"id\":103,\"method\":\"tools/call\",\"params\":{{\"name\":\"search_by_voiceprint\",\"arguments\":{{\"voiceprintId\":\"{vpId1}\",\"minSimilarity\":0.70}}}}}}";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var content = response!["result"]?["content"]?.AsArray();
        Assert.NotNull(content);
        string text = content![0]?["text"]?.GetValue<string>() ?? "";
        Assert.Contains("Morning Standup", text);
        Assert.Contains("spk_alice", text);
        Assert.Contains("Alice (Duplicate)", text);
    }

    [Fact]
    public async Task ToolsCall_SearchByVoiceprint_NotFound_ReturnsToolError()
    {
        var vpRepo = new MockVoiceprintRepository();
        var transcriptRepo = new MockTranscriptRepository();
        var server = new McpServer(transcriptRepo, vpRepo);

        string missingId = Guid.NewGuid().ToString();
        string request = $"{{\"jsonrpc\":\"2.0\",\"id\":104,\"method\":\"tools/call\",\"params\":{{\"name\":\"search_by_voiceprint\",\"arguments\":{{\"voiceprintId\":\"{missingId}\"}}}}}}";
        var response = await server.ProcessMessageAsync(request);

        Assert.NotNull(response);
        var isError = response!["result"]?["isError"]?.GetValue<bool>();
        Assert.True(isError);
        var text = response["result"]?["content"]?.AsArray()?[0]?["text"]?.GetValue<string>();
        Assert.Contains("was not found", text);
    }
}
