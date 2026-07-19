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
    private readonly IConfiguration _configuration;

    public AgentProviderSessionsController(
        ProviderSessionRegistry registry,
        IAuditLogRepository auditLogRepository,
        IConfiguration configuration)
    {
        _registry = registry;
        _auditLogRepository = auditLogRepository;
        _configuration = configuration;
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

        var origin = GetOrigin();
        var baseUrl = GetProviderBaseUrl(origin);
        var internalEventEndpoint = GetInternalEventEndpoint(origin);
        return Ok(new
        {
            sessionId = result.Session.SessionId,
            state = result.Session.State.ToString(),
            result.Session.FailureReason,
            baseUrl,
            model = "agy",
            apiKey = result.PlaintextApiKey,
            harness = BuildHarnessConfig(baseUrl, result.PlaintextApiKey),
            hookBridge = BuildHookBridgeConfig(result.Session, internalEventEndpoint, IsHookBridgeEnabled()),
            connectionManifest = BuildConnectionManifest(result.Session, baseUrl, internalEventEndpoint, IsHookBridgeEnabled(), result.PlaintextApiKey),
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

        var sessions = _registry.GetForUser(ownerUserId).Select(session => ToDto(session, GetOrigin(), IsHookBridgeEnabled()));

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

        return Ok(ToDto(session, GetOrigin(), IsHookBridgeEnabled()));
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

    [HttpPost("{sessionId:guid}/api-key/regenerate")]
    public async Task<IActionResult> RegenerateApiKey(Guid sessionId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var ownerUserId))
        {
            return Unauthorized();
        }

        var result = _registry.RegenerateApiKey(ownerUserId, sessionId);
        if (result == null)
        {
            return NotFound();
        }

        await WriteAuditAsync(ownerUserId, "agent.provider.session.api_key.regenerate", result.Session, "Success", cancellationToken);

        var origin = GetOrigin();
        var baseUrl = GetProviderBaseUrl(origin);
        var internalEventEndpoint = GetInternalEventEndpoint(origin);
        return Ok(new
        {
            sessionId = result.Session.SessionId,
            state = result.Session.State.ToString(),
            baseUrl,
            model = "agy",
            apiKey = result.PlaintextApiKey,
            apiKeyGeneratedAt = result.Session.UpdatedAt,
            oneTimeDisplay = true,
            message = "Store this key now. It will not be shown again after you leave this page.",
            harness = BuildHarnessConfig(baseUrl, result.PlaintextApiKey),
            connectionManifest = BuildConnectionManifest(result.Session, baseUrl, internalEventEndpoint, IsHookBridgeEnabled(), result.PlaintextApiKey),
            expiresAt = result.Session.ExpiresAt
        });
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

    private string GetOrigin() => $"{Request.Scheme}://{Request.Host}";

    private static string GetProviderBaseUrl(string origin) => $"{origin}/v1";

    private static string GetInternalEventEndpoint(string origin) => $"{origin}/api/internal/agent-events";

    private bool IsHookBridgeEnabled() => !string.IsNullOrWhiteSpace(_configuration["AgentRuntime:InternalEventSecret"]);

    private static object ToDto(ProviderSession session, string origin, bool hookBridgeEnabled)
    {
        var baseUrl = GetProviderBaseUrl(origin);
        var internalEventEndpoint = GetInternalEventEndpoint(origin);
        return new
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
        baseUrl,
        model = "agy",
        harness = BuildHarnessConfig(baseUrl, "<apiKey>"),
        hookBridge = BuildHookBridgeConfig(session, internalEventEndpoint, hookBridgeEnabled),
        connectionManifest = BuildConnectionManifest(session, baseUrl, internalEventEndpoint, hookBridgeEnabled, "<apiKey>")
    };
    }

    private static object BuildHarnessConfig(string baseUrl, string apiKey) => new
    {
        baseUrl,
        model = "agy",
        authorization = $"Bearer {apiKey}",
        environment = new Dictionary<string, string>
        {
            ["OPENAI_BASE_URL"] = baseUrl,
            ["OPENAI_API_KEY"] = apiKey,
            ["OPENAI_MODEL"] = "agy",
            ["WEBTERMINAL_PROVIDER_BASE_URL"] = baseUrl,
            ["WEBTERMINAL_PROVIDER_API_KEY"] = apiKey
        }
    };

    private static object BuildHookBridgeConfig(ProviderSession session, string internalEventEndpoint, bool hookBridgeEnabled) => new
    {
        enabled = hookBridgeEnabled,
        endpoint = internalEventEndpoint,
        providerSessionId = session.SessionId,
        scriptPath = "tools/agent-runtime/agy_hook_bridge.py",
        environment = new Dictionary<string, string>
        {
            ["WEBTERMINAL_AGENT_EVENT_ENDPOINT"] = internalEventEndpoint,
            ["WEBTERMINAL_PROVIDER_SESSION_ID"] = session.SessionId.ToString(),
            ["WEBTERMINAL_AGENT_EVENT_SECRET"] = "<configured server secret>"
        },
        command = BuildHookCommand(session, internalEventEndpoint),
        agyHooks = BuildAgyHookConfig(session, internalEventEndpoint)
    };

    private static string BuildHookCommand(ProviderSession session, string internalEventEndpoint) =>
        $"python tools/agent-runtime/agy_hook_bridge.py --endpoint {internalEventEndpoint} --provider-session-id {session.SessionId} --secret <configured server secret>";

    private static object BuildAgyHookConfig(ProviderSession session, string internalEventEndpoint)
    {
        var command = BuildHookCommand(session, internalEventEndpoint);
        return new
        {
            hooks = new Dictionary<string, object>
            {
                ["PreInvocation"] = new { command },
                ["PostInvocation"] = new { command },
                ["Stop"] = new { command }
            },
            environment = new Dictionary<string, string>
            {
                ["WEBTERMINAL_AGENT_EVENT_ENDPOINT"] = internalEventEndpoint,
                ["WEBTERMINAL_PROVIDER_SESSION_ID"] = session.SessionId.ToString(),
                ["WEBTERMINAL_AGENT_EVENT_SECRET"] = "<configured server secret>"
            }
        };
    }

    private static object BuildConnectionManifest(
        ProviderSession session,
        string baseUrl,
        string internalEventEndpoint,
        bool hookBridgeEnabled,
        string apiKey) => new
    {
        version = "webterminal-agent-provider.v1",
        sessionId = session.SessionId,
        displayName = session.DisplayName,
        model = "agy",
        baseUrl,
        expiresAt = session.ExpiresAt,
        openAi = BuildHarnessConfig(baseUrl, apiKey),
        hookBridge = BuildHookBridgeConfig(session, internalEventEndpoint, hookBridgeEnabled),
        smokeTest = new
        {
            scriptPath = "tools/agent-runtime/provider_harness_smoke.py",
            command = $"python tools/agent-runtime/provider_harness_smoke.py --base-url {baseUrl} --api-key {apiKey}",
            liveE2eScriptPath = "tools/agent-runtime/provider_live_e2e.py",
            liveE2eCommand = $"python tools/agent-runtime/provider_live_e2e.py --origin {baseUrl[..^3]} --username <admin username> --password <admin password>"
        }
    };
}

public sealed record CreateProviderSessionRequest(string Profile, string DisplayName);
