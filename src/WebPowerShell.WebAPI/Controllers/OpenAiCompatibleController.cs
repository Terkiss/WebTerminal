using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.AgentRuntime;

namespace WebPowerShell.WebAPI.Controllers;

[ApiController]
[Route("v1")]
[AllowAnonymous]
[EnableRateLimiting("ProviderApiLimiter")]
public sealed class OpenAiCompatibleController : ControllerBase
{
    private readonly ProviderSessionRegistry _registry;
    private readonly IAgyRuntimeManager _runtimeManager;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly TranscriptResponseExtractor _transcriptResponseExtractor;

    public OpenAiCompatibleController(
        ProviderSessionRegistry registry,
        IAgyRuntimeManager runtimeManager,
        IAuditLogRepository auditLogRepository,
        TranscriptResponseExtractor transcriptResponseExtractor)
    {
        _registry = registry;
        _runtimeManager = runtimeManager;
        _auditLogRepository = auditLogRepository;
        _transcriptResponseExtractor = transcriptResponseExtractor;
    }

    [HttpGet("models")]
    public async Task<IActionResult> GetModels(CancellationToken cancellationToken)
    {
        var session = AuthenticateProviderSession();
        if (session == null)
        {
            return Unauthorized(new { error = new { message = "Invalid provider API key.", type = "invalid_api_key" } });
        }

        await WriteAuditAsync(session, "agent.provider.models", "Success", null, cancellationToken);
        return Ok(new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    id = "agy",
                    @object = "model",
                    created = new DateTimeOffset(session.CreatedAt.UtcDateTime).ToUnixTimeSeconds(),
                    owned_by = "webterminal"
                }
            }
        });
    }

    [HttpPost("chat/completions")]
    public async Task<IActionResult> CreateChatCompletion(
        [FromBody] ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var session = AuthenticateProviderSession();
        if (session == null)
        {
            return Unauthorized(new { error = new { message = "Invalid provider API key.", type = "invalid_api_key" } });
        }

        if (request.Messages == null || request.Messages.Count == 0)
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "MessagesRequired", cancellationToken);
            return BadRequest(new { error = new { message = "At least one message is required.", type = "invalid_request_error" } });
        }

        if (request.Model != "agy")
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "UnsupportedModel", cancellationToken);
            return BadRequest(new { error = new { message = "Only model 'agy' is supported.", type = "invalid_request_error" } });
        }

        if (session.State == ProviderSessionState.Failed)
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Failed", session.FailureReason, cancellationToken);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = new
                {
                    message = session.FailureReason ?? "Provider session is not available.",
                    type = "provider_unavailable"
                }
            });
        }

        var isToolResultRequest = IsToolResultRequest(request);
        if (session.State == ProviderSessionState.Generating ||
            (session.State == ProviderSessionState.WaitingForToolResult && !isToolResultRequest))
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "SessionBusy", cancellationToken);
            return Conflict(new { error = new { message = "Provider session is busy.", type = "session_busy" } });
        }

        if (!await session.RequestLock.WaitAsync(0, cancellationToken))
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "SessionBusy", cancellationToken);
            return Conflict(new { error = new { message = "Provider session is busy.", type = "session_busy" } });
        }

        try
        {
            var userPrompt = BuildAgyPrompt(request);

            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "PromptRequired", cancellationToken);
                return BadRequest(new { error = new { message = "A user or tool message is required.", type = "invalid_request_error" } });
            }

            var completion = await _runtimeManager.CompleteAsync(session, userPrompt, cancellationToken);
            if (!completion.IsSuccess)
            {
                await WriteAuditAsync(session, "agent.provider.chat", "Failed", completion.ErrorMessage, cancellationToken);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = new
                    {
                        message = completion.ErrorMessage ?? "AGY completion failed.",
                        type = "provider_error"
                    }
                });
            }

            var output = await ResolveCompletionTextAsync(session, completion.Text, cancellationToken);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var parsed = ParseAgyChatOutput(output);
            if (parsed.ToolCalls.Count > 0)
            {
                session.State = ProviderSessionState.WaitingForToolResult;
                session.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await WriteAuditAsync(
                session,
                parsed.ToolCalls.Count > 0 ? "agent.provider.chat.tool_call" : "agent.provider.chat",
                "Success",
                null,
                cancellationToken);

            if (request.Stream)
            {
                await WriteStreamingCompletionAsync(parsed, now, cancellationToken);
                return new EmptyResult();
            }

            var response = new
            {
                id = $"chatcmpl-{Guid.NewGuid():N}",
                @object = "chat.completion",
                created = now,
                model = "agy",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = parsed.ToMessagePayload(),
                        finish_reason = parsed.ToolCalls.Count > 0 ? "tool_calls" : "stop"
                    }
                },
                usage = new
                {
                    prompt_tokens = 0,
                    completion_tokens = 0,
                    total_tokens = 0
                }
            };

            return Ok(response);
        }
        finally
        {
            session.RequestLock.Release();
        }
    }

    private ProviderSession? AuthenticateProviderSession()
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var apiKey = header[prefix.Length..].Trim();
        return _registry.FindByApiKey(apiKey);
    }

    private async Task<string> ResolveCompletionTextAsync(
        ProviderSession session,
        string? stdoutText,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(stdoutText))
        {
            return stdoutText;
        }

        var delta = await _registry.ReadTranscriptDeltaAsync(session, cancellationToken);
        return _transcriptResponseExtractor.ExtractAssistantOutput(delta) ?? string.Empty;
    }

    private async Task WriteAuditAsync(
        ProviderSession session,
        string command,
        string status,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        await _auditLogRepository.AddAsync(new AuditLog
        {
            UserId = session.OwnerUserId,
            UsernameSnapshot = "provider-api",
            SessionId = session.SessionId.ToString(),
            TabId = "agent-provider",
            Command = command,
            ExecutedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            ResultStatus = status,
            ErrorCode = errorCode,
            CorrelationId = HttpContext.TraceIdentifier
        }, cancellationToken);
    }

    private static bool IsToolResultRequest(ChatCompletionRequest request)
    {
        return request.Messages.LastOrDefault() is { } lastMessage &&
            string.Equals(lastMessage.Role, "tool", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildAgyPrompt(ChatCompletionRequest request)
    {
        var lastMessage = request.Messages.LastOrDefault(message =>
            string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase));

        var content = lastMessage?.GetContentAsString();
        if (lastMessage == null || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        if (string.Equals(lastMessage.Role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
Tool result received.
tool_call_id: {lastMessage.ToolCallId ?? "unknown"}
name: {lastMessage.Name ?? "unknown"}

{content}

Continue from this tool result. If another tool is needed, return the tool call JSON format exactly.
""";
        }

        if (request.Tools is not { Count: > 0 })
        {
            return content;
        }

        const string toolCallFormat =
            """{"tool_calls":[{"id":"call_<unique>","type":"function","function":{"name":"<tool name>","arguments":"<JSON string arguments>"}}]}""";
        var toolsJson = JsonSerializer.Serialize(request.Tools, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return $"""
{lastMessage.Content}

Available external tools are provided below. WebTerminal cannot execute them. If a tool is required, respond with only this JSON object:
{toolCallFormat}

Tools:
{toolsJson}
""";
    }

    public static ParsedAgyChatOutput ParseAgyChatOutput(string text)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new ParsedAgyChatOutput(string.Empty, []);
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (!document.RootElement.TryGetProperty("tool_calls", out var toolCallsElement) ||
                toolCallsElement.ValueKind != JsonValueKind.Array)
            {
                return new ParsedAgyChatOutput(text, []);
            }

            var toolCalls = new List<OpenAiToolCall>();
            foreach (var toolCallElement in toolCallsElement.EnumerateArray())
            {
                if (!toolCallElement.TryGetProperty("function", out var functionElement) ||
                    !functionElement.TryGetProperty("name", out var nameElement))
                {
                    continue;
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var id = toolCallElement.TryGetProperty("id", out var idElement)
                    ? idElement.GetString()
                    : null;
                var type = toolCallElement.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;
                var arguments = functionElement.TryGetProperty("arguments", out var argumentsElement)
                    ? NormalizeArguments(argumentsElement)
                    : "{}";

                toolCalls.Add(new OpenAiToolCall(
                    string.IsNullOrWhiteSpace(id) ? $"call_{Guid.NewGuid():N}" : id,
                    string.IsNullOrWhiteSpace(type) ? "function" : type,
                    new OpenAiFunctionCall(name, arguments)));
            }

            return toolCalls.Count == 0
                ? new ParsedAgyChatOutput(text, [])
                : new ParsedAgyChatOutput(null, toolCalls);
        }
        catch (JsonException)
        {
            return new ParsedAgyChatOutput(text, []);
        }
    }

    private static string NormalizeArguments(JsonElement argumentsElement)
    {
        return argumentsElement.ValueKind == JsonValueKind.String
            ? argumentsElement.GetString() ?? "{}"
            : argumentsElement.GetRawText();
    }

    private async Task WriteStreamingCompletionAsync(ParsedAgyChatOutput parsed, long created, CancellationToken cancellationToken)
    {
        var completionId = $"chatcmpl-{Guid.NewGuid():N}";
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        await WriteSseAsync(new
        {
            id = completionId,
            @object = "chat.completion.chunk",
            created,
            model = "agy",
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new { role = "assistant" },
                    finish_reason = (string?)null
                }
            }
        }, cancellationToken);

        if (parsed.ToolCalls.Count > 0)
        {
            await WriteSseAsync(new
            {
                id = completionId,
                @object = "chat.completion.chunk",
                created,
                model = "agy",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { tool_calls = parsed.ToolCalls },
                        finish_reason = (string?)null
                    }
                }
            }, cancellationToken);
        }
        else if (!string.IsNullOrEmpty(parsed.Content))
        {
            await WriteSseAsync(new
            {
                id = completionId,
                @object = "chat.completion.chunk",
                created,
                model = "agy",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { content = parsed.Content },
                        finish_reason = (string?)null
                    }
                }
            }, cancellationToken);
        }

        await WriteSseAsync(new
        {
            id = completionId,
            @object = "chat.completion.chunk",
            created,
            model = "agy",
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new { },
                    finish_reason = parsed.ToolCalls.Count > 0 ? "tool_calls" : "stop"
                }
            }
        }, cancellationToken);

        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private async Task WriteSseAsync(object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}

public sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream = false,
    [property: JsonPropertyName("tools")] IReadOnlyList<JsonElement>? Tools = null,
    [property: JsonPropertyName("tool_choice")] object? ToolChoice = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("user")] string? User = null,
    [property: JsonPropertyName("metadata")] Dictionary<string, object>? Metadata = null);

public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] JsonElement Content,
    [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null,
    [property: JsonPropertyName("name")] string? Name = null)
{
    public string GetContentAsString()
    {
        return Content.ValueKind switch
        {
            JsonValueKind.String => Content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                Environment.NewLine,
                Content.EnumerateArray()
                    .Select(part => TryGetTextPart(part))
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => Content.GetRawText()
        };
    }

    private static string? TryGetTextPart(JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.String)
        {
            return part.GetString();
        }

        if (part.ValueKind != JsonValueKind.Object)
        {
            return part.GetRawText();
        }

        if (part.TryGetProperty("text", out var textElement))
        {
            return textElement.ValueKind == JsonValueKind.String
                ? textElement.GetString()
                : textElement.GetRawText();
        }

        if (part.TryGetProperty("content", out var contentElement))
        {
            return contentElement.ValueKind == JsonValueKind.String
                ? contentElement.GetString()
                : contentElement.GetRawText();
        }

        return null;
    }
}

public sealed record ParsedAgyChatOutput(string? Content, IReadOnlyList<OpenAiToolCall> ToolCalls)
{
    public object ToMessagePayload()
    {
        if (ToolCalls.Count == 0)
        {
            return new
            {
                role = "assistant",
                content = Content ?? string.Empty
            };
        }

        return new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = ToolCalls
        };
    }
}

public sealed record OpenAiToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OpenAiFunctionCall Function);

public sealed record OpenAiFunctionCall(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments);
