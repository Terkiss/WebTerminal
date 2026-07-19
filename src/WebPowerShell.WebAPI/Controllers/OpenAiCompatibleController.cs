using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
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
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(150);

    private readonly ProviderSessionRegistry _registry;
    private readonly IAgyRuntimeManager _runtimeManager;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly TranscriptResponseExtractor _transcriptResponseExtractor;
    private readonly TimeSpan _requestTimeout;

    public OpenAiCompatibleController(
        ProviderSessionRegistry registry,
        IAgyRuntimeManager runtimeManager,
        IAuditLogRepository auditLogRepository,
        TranscriptResponseExtractor transcriptResponseExtractor,
        IConfiguration configuration)
    {
        _registry = registry;
        _runtimeManager = runtimeManager;
        _auditLogRepository = auditLogRepository;
        _transcriptResponseExtractor = transcriptResponseExtractor;
        _requestTimeout = GetRequestTimeout(configuration);
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


        if (request.Logprobs == true || request.ResponseFormat != null)
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "UnsupportedParameter", cancellationToken);
            return BadRequest(new { error = new { message = "Parameters 'logprobs' and 'response_format' are not supported.", type = "invalid_request_error" } });
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

        if (request.N > 1)
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "UnsupportedN", cancellationToken);
            return BadRequest(new { error = new { message = "Only n=1 is supported.", type = "invalid_request_error" } });
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
        if (!CanAcceptRequest(session.State, isToolResultRequest))
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "ProviderNotReady", cancellationToken);
            return Conflict(new
            {
                error = new
                {
                    message = $"Provider session is not ready. Current state: {session.State}.",
                    type = "provider_not_ready"
                }
            });
        }

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

            if (isToolResultRequest)
            {
                var validationError = await ValidateChatToolResultsAsync(session, request, cancellationToken);
                if (validationError != null)
                {
                    return validationError;
                }
            }

            if (!session.TryAcceptRequestId(GetIdempotencyKey()))
            {
                await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "DuplicateRequest", cancellationToken);
                return Conflict(new { error = new { message = "Duplicate Idempotency-Key for this provider session.", type = "duplicate_request" } });
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_requestTimeout);

            var completion = await _runtimeManager.CompleteAsync(session, userPrompt, timeoutCts.Token);
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
                foreach (var tc in parsed.ToolCalls)
                {
                    session.ExpectedToolCallIds.Add(tc.Id);
                }
            }

            await WriteAuditAsync(
                session,
                parsed.ToolCalls.Count > 0 ? "agent.provider.chat.tool_call" : "agent.provider.chat",
                "Success",
                null,
                cancellationToken);

            if (request.Stream)
            {
                await WriteStreamingCompletionAsync(parsed, now, timeoutCts.Token);
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Failed", "Timeout", CancellationToken.None);
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = new
                {
                    message = "AGY completion timed out.",
                    type = "provider_timeout"
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Cancelled", "ClientDisconnected", CancellationToken.None);
            return new EmptyResult();
        }
        finally
        {
            _registry.Save(session);
            session.RequestLock.Release();
        }
    }

    [HttpPost("responses")]
    public async Task<IActionResult> CreateResponse(
        [FromBody] ResponsesRequest request,
        CancellationToken cancellationToken)
    {
        var session = AuthenticateProviderSession();
        if (session == null)
        {
            return Unauthorized(new { error = new { message = "Invalid provider API key.", type = "invalid_api_key" } });
        }

        if (request.Model != "agy")
        {
            await WriteAuditAsync(session, "agent.provider.responses", "Rejected", "UnsupportedModel", cancellationToken);
            return BadRequest(new { error = new { message = "Only model 'agy' is supported.", type = "invalid_request_error" } });
        }

        if (session.State == ProviderSessionState.Failed)
        {
            await WriteAuditAsync(session, "agent.provider.responses", "Failed", session.FailureReason, cancellationToken);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = new
                {
                    message = session.FailureReason ?? "Provider session is not available.",
                    type = "provider_unavailable"
                }
            });
        }

        var userPrompt = BuildAgyPromptForResponses(request, out var isToolResultRequest, out var toolCallId);
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            await WriteAuditAsync(session, "agent.provider.responses", "Rejected", "PromptRequired", cancellationToken);
            return BadRequest(new { error = new { message = "Input prompt is required.", type = "invalid_request_error" } });
        }

        if (!string.IsNullOrWhiteSpace(request.PreviousResponseId))
        {
            if (!string.Equals(request.PreviousResponseId, session.LastResponseId, StringComparison.Ordinal))
            {
                await WriteAuditAsync(session, "agent.provider.responses", "Rejected", "UnknownPreviousResponseId", cancellationToken);
                return BadRequest(new { error = new { message = $"Unknown previous_response_id: {request.PreviousResponseId}", type = "invalid_request_error" } });
            }
        }

        if (isToolResultRequest)
        {
            var validationError = await ValidateResponseToolResultAsync(session, toolCallId, cancellationToken);
            if (validationError != null)
            {
                return validationError;
            }
        }

        if (!CanAcceptRequest(session.State, isToolResultRequest))
        {
            await WriteAuditAsync(session, "agent.provider.responses", "Rejected", "ProviderNotReady", cancellationToken);
            return Conflict(new
            {
                error = new
                {
                    message = $"Provider session is not ready. Current state: {session.State}.",
                    type = "provider_not_ready"
                }
            });
        }

        if (session.State == ProviderSessionState.Generating ||
            (session.State == ProviderSessionState.WaitingForToolResult && !isToolResultRequest))
        {
            await WriteAuditAsync(session, "agent.provider.responses", "Rejected", "SessionBusy", cancellationToken);
            return Conflict(new { error = new { message = "Provider session is busy.", type = "session_busy" } });
        }

        if (!await session.RequestLock.WaitAsync(0, cancellationToken))
        {
            await WriteAuditAsync(session, "agent.provider.responses", "Rejected", "SessionBusy", cancellationToken);
            return Conflict(new { error = new { message = "Provider session is busy.", type = "session_busy" } });
        }

        try
        {
            if (!session.TryAcceptRequestId(GetIdempotencyKey()))
            {
                await WriteAuditAsync(session, "agent.provider.responses", "Rejected", "DuplicateRequest", cancellationToken);
                return Conflict(new { error = new { message = "Duplicate Idempotency-Key for this provider session.", type = "duplicate_request" } });
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_requestTimeout);

            var completion = await _runtimeManager.CompleteAsync(session, userPrompt, timeoutCts.Token);
            if (!completion.IsSuccess)
            {
                await WriteAuditAsync(session, "agent.provider.responses", "Failed", completion.ErrorMessage, cancellationToken);
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
                foreach (var tc in parsed.ToolCalls)
                {
                    session.ExpectedToolCallIds.Add(tc.Id);
                }
            }

            await WriteAuditAsync(
                session,
                parsed.ToolCalls.Count > 0 ? "agent.provider.responses.tool_call" : "agent.provider.responses",
                "Success",
                null,
                cancellationToken);

            if (request.Stream)
            {
                await WriteStreamingResponseAsync(parsed, now, cancellationToken);
                return new EmptyResult();
            }

            var responseId = $"resp_{Guid.NewGuid():N}";
            session.LastResponseId = responseId;
            var outputItems = new List<object>();

            if (parsed.ToolCalls.Count > 0)
            {
                foreach (var toolCall in parsed.ToolCalls)
                {
                    outputItems.Add(new
                    {
                        id = $"item_{Guid.NewGuid():N}",
                        @object = "response.output_item",
                        type = "function_call",
                        name = toolCall.Function.Name,
                        arguments = toolCall.Function.Arguments,
                        call_id = toolCall.Id
                    });
                }
            }
            else
            {
                outputItems.Add(new
                {
                    id = $"item_{Guid.NewGuid():N}",
                    @object = "response.output_item",
                    type = "message",
                    role = "assistant",
                    content = new[]
                    {
                        new { type = "text", text = parsed.Content ?? string.Empty }
                    }
                });
            }

            var response = new
            {
                id = responseId,
                @object = "response",
                created_at = now,
                model = "agy",
                status = parsed.ToolCalls.Count > 0 ? "requires_action" : "completed",
                output = outputItems,
                usage = new
                {
                    total_tokens = 0,
                    input_tokens = 0,
                    output_tokens = 0
                }
            };

            return Ok(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await WriteAuditAsync(session, "agent.provider.responses", "Failed", "Timeout", CancellationToken.None);
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = new
                {
                    message = "AGY completion timed out.",
                    type = "provider_timeout"
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteAuditAsync(session, "agent.provider.responses", "Cancelled", "ClientDisconnected", CancellationToken.None);
            return new EmptyResult();
        }
        finally
        {
            _registry.Save(session);
            session.RequestLock.Release();
        }
    }

    private static string? BuildAgyPromptForResponses(ResponsesRequest request, out bool isToolResultRequest, out string? toolCallId)
    {
        isToolResultRequest = false;
        toolCallId = null;
        string? content = null;
        string? toolName = null;

        if (request.Input.ValueKind == JsonValueKind.String)
        {
            content = request.Input.GetString();
        }
        else if (request.Input.ValueKind == JsonValueKind.Array)
        {
            var elements = request.Input.EnumerateArray().ToList();
            if (elements.Count > 0)
            {
                var lastMessage = elements[^1];
                if (lastMessage.TryGetProperty("role", out var roleElement) && roleElement.ValueKind == JsonValueKind.String)
                {
                    var role = roleElement.GetString();
                    if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                    {
                        if (lastMessage.TryGetProperty("content", out var contentElement))
                        {
                            content = ChatMessage.GetContentAsString(contentElement);
                        }
                    }
                    else if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
                    {
                        isToolResultRequest = true;
                        if (lastMessage.TryGetProperty("content", out var contentElement))
                        {
                            content = ChatMessage.GetContentAsString(contentElement);
                        }
                        if (lastMessage.TryGetProperty("tool_call_id", out var idElement))
                        {
                            toolCallId = idElement.GetString();
                        }
                        if (lastMessage.TryGetProperty("name", out var nameElement))
                        {
                            toolName = nameElement.GetString();
                        }
                    }
                }
                if (lastMessage.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
                {
                    var type = typeElement.GetString();
                    if (string.Equals(type, "function_call_output", StringComparison.OrdinalIgnoreCase))
                    {
                        isToolResultRequest = true;
                        if (lastMessage.TryGetProperty("output", out var outputElement))
                        {
                            content = outputElement.GetString();
                        }
                        if (lastMessage.TryGetProperty("call_id", out var callIdElement))
                        {
                            toolCallId = callIdElement.GetString();
                        }
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        if (isToolResultRequest)
        {
            return $"""
Tool result received.
tool_call_id: {toolCallId ?? "unknown"}
name: {toolName ?? "unknown"}

{content}

Continue from this tool result. If another tool is needed, return the tool call JSON format exactly.
""";
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.Instructions))
        {
            sb.AppendLine(request.Instructions);
            sb.AppendLine();
        }
        sb.AppendLine(content);

        if (request.Tools is { Count: > 0 } && !IsToolChoiceNone(request.ToolChoice))
        {
            const string toolCallFormat =
                """{"tool_calls":[{"id":"call_<unique>","type":"function","function":{"name":"<tool name>","arguments":"<JSON string arguments>"}}]}""";
            var toolsJson = JsonSerializer.Serialize(request.Tools, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var toolChoiceInstruction = GetToolChoiceInstruction(request.ToolChoice);
            
            sb.AppendLine();
            sb.AppendLine("Available external tools are provided below. WebTerminal cannot execute them. If a tool is required, respond with only this JSON object:");
            sb.AppendLine(toolCallFormat);
            if (!string.IsNullOrEmpty(toolChoiceInstruction))
            {
                sb.AppendLine(toolChoiceInstruction.TrimStart('\r', '\n'));
            }
            sb.AppendLine();
            sb.AppendLine("Tools:");
            sb.Append(toolsJson);
        }

        return sb.ToString();
    }

    private async Task WriteStreamingResponseAsync(ParsedAgyChatOutput parsed, long created, CancellationToken cancellationToken)
    {
        var responseId = $"resp_{Guid.NewGuid():N}";
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var responseData = new
        {
            id = responseId,
            @object = "response",
            created_at = created,
            model = "agy",
            status = "in_progress",
            usage = new { total_tokens = 0, input_tokens = 0, output_tokens = 0 }
        };

        await WriteSseAsync(new
        {
            type = "response.created",
            response = responseData
        }, cancellationToken);

        await WriteSseAsync(new
        {
            type = "response.in_progress",
            response = responseData
        }, cancellationToken);

        if (parsed.ToolCalls.Count > 0)
        {
            foreach (var toolCall in parsed.ToolCalls)
            {
                var itemId = $"item_{Guid.NewGuid():N}";
                await WriteSseAsync(new
                {
                    type = "response.output_item.added",
                    response_id = responseId,
                    output_index = 0,
                    item = new
                    {
                        id = itemId,
                        @object = "response.output_item",
                        type = "function_call",
                        name = toolCall.Function.Name,
                        call_id = toolCall.Id
                    }
                }, cancellationToken);

                await WriteSseAsync(new
                {
                    type = "response.function_call_arguments.delta",
                    response_id = responseId,
                    item_id = itemId,
                    output_index = 0,
                    call_id = toolCall.Id,
                    delta = toolCall.Function.Arguments
                }, cancellationToken);

                await WriteSseAsync(new
                {
                    type = "response.function_call_arguments.done",
                    response_id = responseId,
                    item_id = itemId,
                    output_index = 0,
                    call_id = toolCall.Id,
                    arguments = toolCall.Function.Arguments
                }, cancellationToken);

                await WriteSseAsync(new
                {
                    type = "response.output_item.done",
                    response_id = responseId,
                    output_index = 0,
                    item = new
                    {
                        id = itemId,
                        @object = "response.output_item",
                        type = "function_call",
                        name = toolCall.Function.Name,
                        call_id = toolCall.Id,
                        arguments = toolCall.Function.Arguments
                    }
                }, cancellationToken);
            }
        }
        else if (!string.IsNullOrEmpty(parsed.Content))
        {
            var itemId = $"item_{Guid.NewGuid():N}";
            await WriteSseAsync(new
            {
                type = "response.output_item.added",
                response_id = responseId,
                output_index = 0,
                item = new
                {
                    id = itemId,
                    @object = "response.output_item",
                    type = "message",
                    role = "assistant",
                    content = Array.Empty<object>()
                }
            }, cancellationToken);

            await WriteSseAsync(new
            {
                type = "response.content_part.added",
                response_id = responseId,
                item_id = itemId,
                output_index = 0,
                content_index = 0,
                part = new { type = "text", text = "" }
            }, cancellationToken);

            await WriteSseAsync(new
            {
                type = "response.text.delta",
                response_id = responseId,
                item_id = itemId,
                output_index = 0,
                content_index = 0,
                delta = parsed.Content
            }, cancellationToken);

            await WriteSseAsync(new
            {
                type = "response.content_part.done",
                response_id = responseId,
                item_id = itemId,
                output_index = 0,
                content_index = 0,
                part = new { type = "text", text = parsed.Content }
            }, cancellationToken);

            await WriteSseAsync(new
            {
                type = "response.output_item.done",
                response_id = responseId,
                output_index = 0,
                item = new
                {
                    id = itemId,
                    @object = "response.output_item",
                    type = "message",
                    role = "assistant",
                    content = new[]
                    {
                        new { type = "text", text = parsed.Content }
                    }
                }
            }, cancellationToken);
        }

        var completedStatus = parsed.ToolCalls.Count > 0 ? "requires_action" : "completed";
        await WriteSseAsync(new
        {
            type = "response.done",
            response = new
            {
                id = responseId,
                @object = "response",
                created_at = created,
                model = "agy",
                status = completedStatus,
                usage = new { total_tokens = 0, input_tokens = 0, output_tokens = 0 }
            }
        }, cancellationToken);

        await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static TimeSpan GetRequestTimeout(IConfiguration configuration)
    {
        return int.TryParse(configuration["AgentRuntime:RequestTimeoutSeconds"], out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultRequestTimeout;
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

    private async Task<IActionResult?> ValidateChatToolResultsAsync(
        ProviderSession session,
        ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var toolMessage in GetNewToolMessages(request))
        {
            if (string.IsNullOrWhiteSpace(toolMessage.ToolCallId) ||
                !session.ExpectedToolCallIds.Contains(toolMessage.ToolCallId) ||
                !resultIds.Add(toolMessage.ToolCallId))
            {
                await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "UnknownToolCallId", cancellationToken);
                return InvalidRequest($"Unknown or duplicate tool_call_id: {toolMessage.ToolCallId}");
            }
        }

        if (resultIds.Count != session.ExpectedToolCallIds.Count)
        {
            await WriteAuditAsync(session, "agent.provider.chat", "Rejected", "MissingToolResults", cancellationToken);
            return InvalidRequest("All pending tool results must be provided.");
        }

        foreach (var resultId in resultIds)
        {
            session.ExpectedToolCallIds.Remove(resultId);
        }

        return null;
    }

    private async Task<IActionResult?> ValidateResponseToolResultAsync(
        ProviderSession session,
        string? toolCallId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolCallId) || !session.ExpectedToolCallIds.Contains(toolCallId))
        {
            await WriteAuditAsync(session, "agent.provider.responses", "Rejected", "UnknownToolCallId", cancellationToken);
            return InvalidRequest($"Unknown or duplicate call_id: {toolCallId}");
        }

        if (session.ExpectedToolCallIds.Count != 1)
        {
            await WriteAuditAsync(session, "agent.provider.responses", "Rejected", "MissingToolResults", cancellationToken);
            return InvalidRequest("All pending tool results must be provided.");
        }

        session.ExpectedToolCallIds.Remove(toolCallId);
        return null;
    }

    private static IEnumerable<ChatMessage> GetNewToolMessages(ChatCompletionRequest request)
    {
        for (var i = request.Messages.Count - 1; i >= 0; i--)
        {
            var message = request.Messages[i];
            if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                yield return message;
            }
        }
    }

    private BadRequestObjectResult InvalidRequest(string message)
    {
        return BadRequest(new { error = new { message, type = "invalid_request_error" } });
    }

    private string? GetIdempotencyKey()
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        return string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey;
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

    private static bool CanAcceptRequest(ProviderSessionState state, bool isToolResultRequest)
    {
        return state switch
        {
            ProviderSessionState.Ready or ProviderSessionState.WaitingForRequest => true,
            ProviderSessionState.WaitingForToolResult => isToolResultRequest,
            _ => false
        };
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
            var matchingToolCall = FindMatchingToolCall(request, lastMessage.ToolCallId);
            var toolCallContext = matchingToolCall.HasValue
                ? $"""
Original tool call:
{matchingToolCall.Value.GetRawText()}

"""
                : string.Empty;

            return $"""
Tool result received.
tool_call_id: {lastMessage.ToolCallId ?? "unknown"}
name: {lastMessage.Name ?? "unknown"}

{toolCallContext}
{content}

Continue from this tool result. If another tool is needed, return the tool call JSON format exactly.
""";
        }

        if (request.Tools is not { Count: > 0 } || IsToolChoiceNone(request.ToolChoice))
        {
            return content;
        }

        const string toolCallFormat =
            """{"tool_calls":[{"id":"call_<unique>","type":"function","function":{"name":"<tool name>","arguments":"<JSON string arguments>"}}]}""";
        var toolsJson = JsonSerializer.Serialize(request.Tools, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var toolChoiceInstruction = GetToolChoiceInstruction(request.ToolChoice);
        return $"""
{content}

Available external tools are provided below. WebTerminal cannot execute them. If a tool is required, respond with only this JSON object:
{toolCallFormat}
{toolChoiceInstruction}

Tools:
{toolsJson}
""";
    }

    private static bool IsToolChoiceNone(JsonElement? toolChoice)
    {
        return toolChoice.HasValue &&
            toolChoice.Value.ValueKind == JsonValueKind.String &&
            string.Equals(toolChoice.Value.GetString(), "none", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetToolChoiceInstruction(JsonElement? toolChoice)
    {
        if (!toolChoice.HasValue)
        {
            return string.Empty;
        }

        var value = toolChoice.Value;
        if (value.ValueKind == JsonValueKind.String)
        {
            return string.Equals(value.GetString(), "required", StringComparison.OrdinalIgnoreCase)
                ? $"{Environment.NewLine}Tool choice: you must return a tool call."
                : string.Empty;
        }

        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("function", out var functionElement) &&
            functionElement.ValueKind == JsonValueKind.Object &&
            functionElement.TryGetProperty("name", out var nameElement) &&
            nameElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            return $"{Environment.NewLine}Tool choice: call only the function named {nameElement.GetString()}.";
        }

        return string.Empty;
    }

    private static JsonElement? FindMatchingToolCall(ChatCompletionRequest request, string? toolCallId)
    {
        if (string.IsNullOrWhiteSpace(toolCallId))
        {
            return null;
        }

        foreach (var message in request.Messages.Reverse())
        {
            if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) ||
                message.ToolCalls is not { ValueKind: JsonValueKind.Array } toolCalls)
            {
                continue;
            }

            foreach (var toolCall in toolCalls.EnumerateArray())
            {
                if (toolCall.ValueKind == JsonValueKind.Object &&
                    toolCall.TryGetProperty("id", out var idElement) &&
                    string.Equals(idElement.GetString(), toolCallId, StringComparison.Ordinal))
                {
                    return toolCall.Clone();
                }
            }
        }

        return null;
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
                        delta = new { tool_calls = ToStreamingToolCalls(parsed.ToolCalls) },
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

    private static IReadOnlyList<object> ToStreamingToolCalls(IReadOnlyList<OpenAiToolCall> toolCalls)
    {
        return toolCalls
            .Select((toolCall, index) => new
            {
                index,
                id = toolCall.Id,
                type = toolCall.Type,
                function = new
                {
                    name = toolCall.Function.Name,
                    arguments = toolCall.Function.Arguments
                }
            })
            .Cast<object>()
            .ToList();
    }

    private async Task WriteSseAsync(object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
    [Route("/v1/{*path}")]
    [HttpGet]
    [HttpPost]
    [HttpPut]
    [HttpDelete]
    [HttpPatch]
    [HttpOptions]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult UnsupportedApi(string path)
    {
        return BadRequest(new { error = new { message = $"Unsupported API endpoint: /v1/{path}", type = "invalid_request_error" } });
    }
}

public sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream = false,
    [property: JsonPropertyName("tools")] IReadOnlyList<JsonElement>? Tools = null,
    [property: JsonPropertyName("tool_choice")] JsonElement? ToolChoice = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("user")] string? User = null,
    [property: JsonPropertyName("metadata")] Dictionary<string, object>? Metadata = null,
    [property: JsonPropertyName("stop")] JsonElement? Stop = null,
    [property: JsonPropertyName("response_format")] JsonElement? ResponseFormat = null,
    [property: JsonPropertyName("seed")] int? Seed = null,
    [property: JsonPropertyName("n")] int? N = null,
    [property: JsonPropertyName("top_p")] double? TopP = null,
    [property: JsonPropertyName("presence_penalty")] double? PresencePenalty = null,
    [property: JsonPropertyName("frequency_penalty")] double? FrequencyPenalty = null,
    [property: JsonPropertyName("logprobs")] bool? Logprobs = null,
    [property: JsonPropertyName("top_logprobs")] int? TopLogprobs = null,
    [property: JsonPropertyName("parallel_tool_calls")] bool? ParallelToolCalls = null);

public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] JsonElement Content,
    [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("tool_calls")] JsonElement? ToolCalls = null)
{
    public string GetContentAsString() => GetContentAsString(Content);

    public static string GetContentAsString(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                Environment.NewLine,
                content.EnumerateArray()
                    .Select(part => TryGetTextPart(part))
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => content.GetRawText()
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

public sealed record ResponsesRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] JsonElement Input,
    [property: JsonPropertyName("instructions")] string? Instructions = null,
    [property: JsonPropertyName("tools")] IReadOnlyList<JsonElement>? Tools = null,
    [property: JsonPropertyName("tool_choice")] JsonElement? ToolChoice = null,
    [property: JsonPropertyName("parallel_tool_calls")] bool? ParallelToolCalls = null,
    [property: JsonPropertyName("previous_response_id")] string? PreviousResponseId = null,
    [property: JsonPropertyName("stream")] bool Stream = false,
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("max_output_tokens")] int? MaxOutputTokens = null,
    [property: JsonPropertyName("metadata")] Dictionary<string, object>? Metadata = null,
    [property: JsonPropertyName("user")] string? User = null);
