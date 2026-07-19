using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.AgentRuntime;

namespace WebPowerShell.WebAPI.Controllers;

[ApiController]
[Route("api/agent/provider-sessions")]
[Authorize(Roles = "Admin")]
public sealed class AgentProviderSessionsController : ControllerBase
{
    private readonly ProviderSessionRegistry _registry;
    private readonly IAuditLogRepository _auditLogRepository;

    public AgentProviderSessionsController(
        ProviderSessionRegistry registry,
        IAuditLogRepository auditLogRepository)
    {
        _registry = registry;
        _auditLogRepository = auditLogRepository;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProviderSessionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var ownerUserId))
        {
            return Unauthorized();
        }

        var result = await _registry.CreateAsync(
            ownerUserId,
            request.Profile,
            request.DisplayName,
            cancellationToken);
        await WriteAuditAsync(ownerUserId, "agent.provider.session.create", result.Session, "Success", cancellationToken);

        var origin = $"{Request.Scheme}://{Request.Host}";
        var baseUrl = $"{origin}/v1";
        var internalEventEndpoint = $"{origin}/api/internal/agent-events";
        return Ok(new
        {
            sessionId = result.Session.SessionId,
            state = result.Session.State.ToString(),
            result.Session.FailureReason,
            baseUrl,
            model = "agy",
            apiKey = result.PlaintextApiKey,
            harness = new
            {
                baseUrl,
                model = "agy",
                authorization = "Bearer <apiKey>"
            },
            hookBridge = new
            {
                endpoint = internalEventEndpoint,
                providerSessionId = result.Session.SessionId,
                scriptPath = "tools/agent-runtime/agy_hook_bridge.py",
                environment = new
                {
                    WEBTERMINAL_AGENT_EVENT_ENDPOINT = internalEventEndpoint,
                    WEBTERMINAL_PROVIDER_SESSION_ID = result.Session.SessionId.ToString(),
                    WEBTERMINAL_AGENT_EVENT_SECRET = "<configured server secret>"
                }
            },
            expiresAt = result.Session.ExpiresAt,
            agy = new
            {
                available = result.RuntimeProbe.IsAvailable,
                executablePath = result.RuntimeProbe.ExecutablePath
            }
        });
    }

    [HttpGet]
    public IActionResult List()
    {
        if (!TryGetUserId(out var ownerUserId))
        {
            return Unauthorized();
        }

        var sessions = _registry.GetForUser(ownerUserId).Select(ToDto);

        return Ok(sessions);
    }

    [HttpGet("{sessionId:guid}")]
    public IActionResult Get(Guid sessionId)
    {
        if (!TryGetUserId(out var ownerUserId))
        {
            return Unauthorized();
        }

        var session = _registry.GetByIdForUser(ownerUserId, sessionId);
        if (session == null)
        {
            return NotFound();
        }

        return Ok(ToDto(session));
    }

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Revoke(Guid sessionId)
    {
        if (!TryGetUserId(out var ownerUserId))
        {
            return Unauthorized();
        }

        var session = _registry.GetByIdForUser(ownerUserId, sessionId);
        if (!_registry.Revoke(ownerUserId, sessionId))
        {
            return NotFound();
        }

        await WriteAuditAsync(ownerUserId, "agent.provider.session.revoke", session, "Success", CancellationToken.None);
        return Ok(new { success = true });
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private async Task WriteAuditAsync(
        Guid userId,
        string command,
        ProviderSession? session,
        string status,
        CancellationToken cancellationToken)
    {
        await _auditLogRepository.AddAsync(new AuditLog
        {
            UserId = userId,
            UsernameSnapshot = User.Identity?.Name ?? string.Empty,
            SessionId = session?.SessionId.ToString() ?? string.Empty,
            TabId = "agent-provider",
            Command = command,
            ExecutedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            ResultStatus = status,
            ErrorCode = session?.FailureReason,
            CorrelationId = HttpContext.TraceIdentifier
        }, cancellationToken);
    }

    private static object ToDto(ProviderSession session) => new
    {
        session.SessionId,
        session.DisplayName,
        session.Profile,
        state = session.State.ToString(),
        session.ConversationId,
        session.AgyProcessId,
        session.LastTranscriptPath,
        session.TranscriptOffset,
        session.CreatedAt,
        session.UpdatedAt,
        session.ExpiresAt,
        session.FailureReason,
        hookBridge = new
        {
            scriptPath = "tools/agent-runtime/agy_hook_bridge.py",
            environment = new
            {
                WEBTERMINAL_PROVIDER_SESSION_ID = session.SessionId.ToString(),
                WEBTERMINAL_AGENT_EVENT_SECRET = "<configured server secret>"
            }
        }
    };
}

public sealed record CreateProviderSessionRequest(string Profile, string DisplayName);
