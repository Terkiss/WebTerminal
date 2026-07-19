using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.AgentRuntime;

namespace WebPowerShell.WebAPI.Controllers;

[ApiController]
[Route("api/internal/agent-events")]
[AllowAnonymous]
public sealed class InternalAgentEventsController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> SeenNonces = new(StringComparer.Ordinal);
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMinutes(5);

    private readonly ProviderSessionRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InternalAgentEventsController> _logger;

    public InternalAgentEventsController(
        ProviderSessionRegistry registry,
        IConfiguration configuration,
        IAuditLogRepository auditLogRepository,
        TimeProvider timeProvider,
        ILogger<InternalAgentEventsController> logger)
    {
        _registry = registry;
        _configuration = configuration;
        _auditLogRepository = auditLogRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (!VerifyRequest(body, out var failure))
        {
            return Unauthorized(new { error = failure });
        }

        AgentEventRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<AgentEventRequest>(
                body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid JSON body." });
        }

        if (request == null ||
            request.ProviderSessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.EventType))
        {
            return BadRequest(new { error = "providerSessionId and eventType are required." });
        }

        var eventId = string.IsNullOrWhiteSpace(request.EventId)
            ? BuildEventId(request)
            : request.EventId;
        var timestamp = request.Timestamp ?? _timeProvider.GetUtcNow();
        var runtimeEvent = new AgentRuntimeEvent(
            eventId,
            request.EventType,
            request.ProviderSessionId,
            request.ConversationId,
            request.StepIdx,
            request.TranscriptPath,
            timestamp);
        var result = _registry.ApplyEvent(runtimeEvent);
        var transcriptEntries = 0;
        var transcriptOffset = 0L;
        var session = _registry.GetById(runtimeEvent.ProviderSessionId);

        if (result == AgentRuntimeEventResult.Accepted)
        {
            if (session != null)
            {
                var delta = await _registry.ReadTranscriptDeltaAsync(session, cancellationToken);
                transcriptEntries = delta.Entries.Count;
                transcriptOffset = delta.Offset;
            }
        }

        await WriteAuditAsync(session, runtimeEvent, result, cancellationToken);

        return result switch
        {
            AgentRuntimeEventResult.Accepted => Ok(new { status = "accepted", eventId, transcriptEntries, transcriptOffset }),
            AgentRuntimeEventResult.Duplicate => Ok(new { status = "duplicate", eventId }),
            AgentRuntimeEventResult.SessionNotFound => NotFound(new { error = "Provider session was not found." }),
            AgentRuntimeEventResult.SessionExpired => StatusCode(StatusCodes.Status410Gone, new { error = "Provider session expired." }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private bool VerifyRequest(string body, out string failure)
    {
        failure = string.Empty;
        var secret = _configuration["AgentRuntime:InternalEventSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("AgentRuntime:InternalEventSecret is not configured; internal agent events are disabled.");
            failure = "Internal agent events are not configured.";
            return false;
        }

        var timestampValue = Request.Headers["X-Agent-Event-Timestamp"].ToString();
        var nonce = Request.Headers["X-Agent-Event-Nonce"].ToString();
        var signature = Request.Headers["X-Agent-Event-Signature"].ToString();
        if (!long.TryParse(timestampValue, out var timestampSeconds) ||
            string.IsNullOrWhiteSpace(nonce) ||
            string.IsNullOrWhiteSpace(signature))
        {
            failure = "Missing internal event authentication headers.";
            return false;
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
        if ((_timeProvider.GetUtcNow() - timestamp).Duration() > TimestampTolerance)
        {
            failure = "Internal event timestamp is outside the allowed window.";
            return false;
        }

        CleanupNonces();
        if (!SeenNonces.TryAdd($"{timestampValue}:{nonce}", timestamp))
        {
            failure = "Internal event nonce was already used.";
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestampValue}.{nonce}.{body}"));
        var expected = Convert.ToHexString(expectedBytes).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant())))
        {
            failure = "Invalid internal event signature.";
            return false;
        }

        return true;
    }

    private void CleanupNonces()
    {
        var cutoff = _timeProvider.GetUtcNow() - TimestampTolerance;
        foreach (var pair in SeenNonces)
        {
            if (pair.Value < cutoff)
            {
                SeenNonces.TryRemove(pair.Key, out _);
            }
        }
    }

    private static string BuildEventId(AgentEventRequest request)
    {
        var step = request.StepIdx?.ToString() ?? "none";
        var conversation = string.IsNullOrWhiteSpace(request.ConversationId)
            ? "none"
            : request.ConversationId;
        return $"{conversation}:{request.EventType}:{step}";
    }

    private async Task WriteAuditAsync(
        ProviderSession? session,
        AgentRuntimeEvent runtimeEvent,
        AgentRuntimeEventResult result,
        CancellationToken cancellationToken)
    {
        var status = result == AgentRuntimeEventResult.Accepted || result == AgentRuntimeEventResult.Duplicate
            ? "Success"
            : "Rejected";

        await _auditLogRepository.AddAsync(new AuditLog
        {
            UserId = session?.OwnerUserId ?? Guid.Empty,
            UsernameSnapshot = "agent-hook",
            SessionId = runtimeEvent.ProviderSessionId.ToString(),
            TabId = "agent-provider",
            Command = $"agent.provider.event.{runtimeEvent.EventType}",
            ExecutedAt = _timeProvider.GetUtcNow(),
            CompletedAt = _timeProvider.GetUtcNow(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            ResultStatus = status,
            ErrorCode = result == AgentRuntimeEventResult.Accepted ? null : result.ToString(),
            CorrelationId = runtimeEvent.EventId
        }, cancellationToken);
    }
}

public sealed record AgentEventRequest(
    [property: JsonPropertyName("eventId")] string? EventId,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("providerSessionId")] Guid ProviderSessionId,
    [property: JsonPropertyName("conversationId")] string? ConversationId,
    [property: JsonPropertyName("stepIdx")] int? StepIdx,
    [property: JsonPropertyName("transcriptPath")] string? TranscriptPath,
    [property: JsonPropertyName("timestamp")] DateTimeOffset? Timestamp);
