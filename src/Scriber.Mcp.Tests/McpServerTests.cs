namespace Scriber.Mcp.Tests;

using System.Text.Json.Nodes;
using Scriber.Core.Domain;
using Scriber.Mcp;
using Scriber.Storage;
using Xunit;

public class McpServerTests
{
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
}
