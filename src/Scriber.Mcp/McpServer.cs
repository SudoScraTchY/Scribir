namespace Scriber.Mcp;

using System.Text.Json;
using System.Text.Json.Nodes;
using Scriber.Core.Domain;
using Scriber.Export;
using Scriber.Storage;

public sealed class McpServer
{
    private readonly ITranscriptRepository _repository;
    private readonly MarkdownExporter _exporter = new();
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public McpServer(ITranscriptRepository repository, TextReader? input = null, TextWriter? output = null, TextWriter? error = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
        _error = error ?? Console.Error;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _error.WriteLine("[Scriber.Mcp] Server started. Listening on stdio for JSON-RPC 2.0 messages.");

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await _input.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                break; // End of stream
            }

            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var response = await ProcessMessageAsync(line, cancellationToken);
                if (response != null)
                {
                    await _output.WriteLineAsync(response.ToJsonString(JsonOptions));
                    await _output.FlushAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _error.WriteLine($"[Scriber.Mcp] Error processing line: {ex.Message}");
            }
        }

        _error.WriteLine("[Scriber.Mcp] Server stopped.");
    }

    public async Task<JsonObject?> ProcessMessageAsync(string json, CancellationToken cancellationToken = default)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse(null, -32700, $"Parse error: {ex.Message}");
        }

        if (root is not JsonObject request)
        {
            return CreateErrorResponse(null, -32600, "Invalid Request: root must be an object.");
        }

        var idNode = request["id"];
        string? method = request["method"]?.GetValue<string>();
        var paramsNode = request["params"] as JsonObject;

        // If no method, invalid
        if (string.IsNullOrEmpty(method))
        {
            return CreateErrorResponse(idNode, -32600, "Invalid Request: missing method.");
        }

        // Notification (no ID)
        if (idNode == null)
        {
            HandleNotification(method, paramsNode);
            return null;
        }

        try
        {
            var result = await HandleRequestAsync(method, paramsNode, cancellationToken);
            return CreateSuccessResponse(idNode, result);
        }
        catch (McpRpcException ex)
        {
            return CreateErrorResponse(idNode, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(idNode, -32603, $"Internal error: {ex.Message}");
        }
    }

    private void HandleNotification(string method, JsonObject? @params)
    {
        switch (method)
        {
            case "notifications/initialized":
            case "initialized":
                _error.WriteLine("[Scriber.Mcp] Client acknowledged initialization.");
                break;
            default:
                _error.WriteLine($"[Scriber.Mcp] Unhandled notification: {method}");
                break;
        }
    }

    private async Task<JsonObject> HandleRequestAsync(string method, JsonObject? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "initialize" => HandleInitialize(@params),
            "ping" => new JsonObject(),
            "tools/list" => HandleToolsList(),
            "tools/call" => await HandleToolsCallAsync(@params, cancellationToken),
            "resources/list" => await HandleResourcesListAsync(cancellationToken),
            "resources/read" => await HandleResourcesReadAsync(@params, cancellationToken),
            _ => throw new McpRpcException(-32601, $"Method '{method}' not found.")
        };
    }

    private static JsonObject HandleInitialize(JsonObject? @params)
    {
        return new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject(),
                ["resources"] = new JsonObject
                {
                    ["subscribe"] = false,
                    ["listChanged"] = false
                }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "scriber-mcp",
                ["version"] = "1.0.0"
            }
        };
    }

    private static JsonObject HandleToolsList()
    {
        var tools = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "list_transcripts",
                ["description"] = "List transcripts stored in the Scriber library with duration and timestamps.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["limit"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Maximum number of transcripts to return (default 20)"
                        },
                        ["offset"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Pagination offset (default 0)"
                        }
                    }
                }
            },
            new JsonObject
            {
                ["name"] = "search_transcripts",
                ["description"] = "Full-text search across all transcribed recordings in the Scriber library.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Text query terms to search for across transcript segments"
                        },
                        ["limit"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Maximum number of matching segments to return (default 10)"
                        }
                    },
                    ["required"] = new JsonArray { "query" }
                }
            },
            new JsonObject
            {
                ["name"] = "get_transcript",
                ["description"] = "Fetch a transcript by ID in structured Markdown format with YAML frontmatter per ADR-0008.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["id"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Transcript GUID identifier"
                        },
                        ["includeTimestamps"] = new JsonObject
                        {
                            ["type"] = "boolean",
                            ["description"] = "Include per-segment timestamps in Markdown output (default true)"
                        }
                    },
                    ["required"] = new JsonArray { "id" }
                }
            }
        };

        return new JsonObject { ["tools"] = tools };
    }

    private async Task<JsonObject> HandleToolsCallAsync(JsonObject? @params, CancellationToken cancellationToken)
    {
        if (@params == null) throw new McpRpcException(-32602, "Missing params for tools/call");

        string? toolName = @params["name"]?.GetValue<string>();
        var arguments = @params["arguments"] as JsonObject ?? new JsonObject();

        if (string.IsNullOrEmpty(toolName))
        {
            throw new McpRpcException(-32602, "Missing tool name in tools/call");
        }

        return toolName switch
        {
            "list_transcripts" => await ExecuteListTranscriptsAsync(arguments, cancellationToken),
            "search_transcripts" => await ExecuteSearchTranscriptsAsync(arguments, cancellationToken),
            "get_transcript" => await ExecuteGetTranscriptAsync(arguments, cancellationToken),
            _ => throw new McpRpcException(-32601, $"Unknown tool: '{toolName}'")
        };
    }

    private async Task<JsonObject> ExecuteListTranscriptsAsync(JsonObject args, CancellationToken cancellationToken)
    {
        int limit = args["limit"]?.GetValue<int?>() ?? 20;
        int offset = args["offset"]?.GetValue<int?>() ?? 0;

        var all = await _repository.ListAsync(cancellationToken);
        var paged = all.Skip(offset).Take(limit).ToList();

        var textResult = JsonSerializer.Serialize(paged.Select(t => new
        {
            id = t.Id.ToString(),
            title = t.Title,
            createdAt = t.CreatedAt.ToString("o"),
            duration = t.Duration.ToString(),
            speakerCount = t.SpeakerCount,
            segmentCount = t.SegmentCount
        }), new JsonSerializerOptions { WriteIndented = true });

        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = textResult
                }
            },
            ["isError"] = false
        };
    }

    private async Task<JsonObject> ExecuteSearchTranscriptsAsync(JsonObject args, CancellationToken cancellationToken)
    {
        string? query = args["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return CreateToolErrorResult("Missing required parameter 'query'");
        }

        int limit = args["limit"]?.GetValue<int?>() ?? 10;

        var results = await _repository.SearchAsync(query, cancellationToken);
        var top = results.Take(limit).ToList();

        var textResult = JsonSerializer.Serialize(top.Select(r => new
        {
            transcriptId = r.TranscriptId.ToString(),
            transcriptTitle = r.TranscriptTitle,
            speaker = r.SpeakerId,
            start = r.Start.ToString(),
            end = r.End.ToString(),
            snippet = r.Snippet
        }), new JsonSerializerOptions { WriteIndented = true });

        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = textResult
                }
            },
            ["isError"] = false
        };
    }

    private async Task<JsonObject> ExecuteGetTranscriptAsync(JsonObject args, CancellationToken cancellationToken)
    {
        string? idStr = args["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(idStr) || !Guid.TryParse(idStr, out var id))
        {
            return CreateToolErrorResult("Invalid or missing 'id' parameter (must be a valid GUID).");
        }

        bool includeTimestamps = args["includeTimestamps"]?.GetValue<bool?>() ?? true;

        var transcript = await _repository.GetByIdAsync(id, cancellationToken);
        if (transcript == null)
        {
            return CreateToolErrorResult($"Transcript with ID '{id}' was not found.");
        }

        var options = new ExportOptions
        {
            IncludeTimestamps = includeTimestamps,
            IncludeMarkdownWordMetadata = false
        };

        string markdown = _exporter.Export(transcript, options);

        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = markdown
                }
            },
            ["isError"] = false
        };
    }

    private async Task<JsonObject> HandleResourcesListAsync(CancellationToken cancellationToken)
    {
        var summaries = await _repository.ListAsync(cancellationToken);
        var resources = new JsonArray();

        foreach (var s in summaries)
        {
            resources.Add(new JsonObject
            {
                ["uri"] = $"transcript://{s.Id}",
                ["name"] = s.Title,
                ["description"] = $"Transcript created {s.CreatedAt:yyyy-MM-dd HH:mm}, duration: {s.Duration:mm\\:ss}, {s.SpeakerCount} speakers",
                ["mimeType"] = "text/markdown"
            });
        }

        return new JsonObject { ["resources"] = resources };
    }

    private async Task<JsonObject> HandleResourcesReadAsync(JsonObject? @params, CancellationToken cancellationToken)
    {
        if (@params == null) throw new McpRpcException(-32602, "Missing params for resources/read");

        string? uri = @params["uri"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("transcript://", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpRpcException(-32602, "Invalid URI. Expected format: transcript://{guid}");
        }

        string idPart = uri.Substring("transcript://".Length).Trim();
        if (!Guid.TryParse(idPart, out var id))
        {
            throw new McpRpcException(-32602, $"Invalid GUID in URI: '{idPart}'");
        }

        var transcript = await _repository.GetByIdAsync(id, cancellationToken);
        if (transcript == null)
        {
            throw new McpRpcException(-32004, $"Transcript '{id}' not found.");
        }

        string markdown = _exporter.Export(transcript);

        return new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = "text/markdown",
                    ["text"] = markdown
                }
            }
        };
    }

    private static JsonObject CreateToolErrorResult(string message)
    {
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"Error: {message}"
                }
            },
            ["isError"] = true
        };
    }

    private static JsonObject CreateSuccessResponse(JsonNode id, JsonObject result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result
        };
    }

    private static JsonObject CreateErrorResponse(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id != null ? id.DeepClone() : null,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }
}

public sealed class McpRpcException : Exception
{
    public int Code { get; }

    public McpRpcException(int code, string message) : base(message)
    {
        Code = code;
    }
}
