using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace WebPowerShell.Infrastructure.AgentRuntime;

public sealed class ProviderSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, ProviderSession> _sessions = new();
    private readonly ConcurrentDictionary<string, byte> _seenEventIds = new(StringComparer.Ordinal);
    private readonly ILogger<ProviderSessionRegistry> _logger;
    private readonly AgyRuntimeProbe _runtimeProbe;
    private readonly IAgyRuntimeManager _runtimeManager;
    private readonly TranscriptDeltaReader _transcriptDeltaReader;
    private readonly TimeProvider _timeProvider;

    public ProviderSessionRegistry(
        ILogger<ProviderSessionRegistry> logger,
        AgyRuntimeProbe runtimeProbe,
        IAgyRuntimeManager runtimeManager,
        TranscriptDeltaReader transcriptDeltaReader,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _runtimeProbe = runtimeProbe;
        _runtimeManager = runtimeManager;
        _transcriptDeltaReader = transcriptDeltaReader;
        _timeProvider = timeProvider;
    }

    public async Task<CreateProviderSessionResult> CreateAsync(
        Guid ownerUserId,
        string profile,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GenerateApiKey();
        var probe = await _runtimeProbe.ProbeAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();

        var session = new ProviderSession
        {
            OwnerUserId = ownerUserId,
            Profile = string.IsNullOrWhiteSpace(profile) ? "agy-default" : profile.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "AGY Provider Session" : displayName.Trim(),
            ApiKeyHash = ProviderSession.HashApiKey(apiKey),
            State = ProviderSessionState.Created,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddHours(8),
            FailureReason = probe.ErrorMessage
        };

        _sessions[session.SessionId] = session;
        if (probe.IsAvailable)
        {
            await _runtimeManager.PrepareSessionAsync(session, cancellationToken);
        }
        else
        {
            session.State = ProviderSessionState.Failed;
            session.FailureReason = probe.ErrorMessage;
        }

        _logger.LogInformation(
            "Created provider session {SessionId} for user {UserId}; AGY available: {IsAvailable}",
            session.SessionId,
            ownerUserId,
            probe.IsAvailable);

        return new CreateProviderSessionResult(session, apiKey, probe);
    }

    public ProviderSession? FindByApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var hash = ProviderSession.HashApiKey(apiKey);
        var now = _timeProvider.GetUtcNow();

        return _sessions.Values.FirstOrDefault(session =>
            session.State != ProviderSessionState.Stopped &&
            !session.IsExpired(now) &&
            CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(session.ApiKeyHash),
                Convert.FromHexString(hash)));
    }

    public ProviderSession? GetByIdForUser(Guid ownerUserId, Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        if (session.OwnerUserId != ownerUserId || session.IsExpired(_timeProvider.GetUtcNow()))
        {
            return null;
        }

        return session;
    }

    public ProviderSession? GetById(Guid sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session)
            ? session
            : null;
    }

    public IReadOnlyList<ProviderSession> GetForUser(Guid ownerUserId)
    {
        var now = _timeProvider.GetUtcNow();
        return _sessions.Values
            .Where(session => session.OwnerUserId == ownerUserId && !session.IsExpired(now))
            .OrderByDescending(session => session.CreatedAt)
            .ToList();
    }

    public bool Revoke(Guid ownerUserId, Guid sessionId)
    {
        var session = GetByIdForUser(ownerUserId, sessionId);
        if (session == null)
        {
            return false;
        }

        session.State = ProviderSessionState.Stopped;
        session.AgyProcessId = null;
        session.UpdatedAt = _timeProvider.GetUtcNow();
        session.FailureReason = "Provider session was revoked.";
        _logger.LogInformation("Revoked provider session {SessionId} for user {UserId}", sessionId, ownerUserId);
        return true;
    }

    public AgentRuntimeEventResult ApplyEvent(AgentRuntimeEvent runtimeEvent)
    {
        if (!_seenEventIds.TryAdd(runtimeEvent.EventId, 0))
        {
            return AgentRuntimeEventResult.Duplicate;
        }

        var session = GetByEvent(runtimeEvent);
        if (session == null)
        {
            _logger.LogWarning(
                "Rejected AGY event {EventId} because provider session {ProviderSessionId} was not found",
                runtimeEvent.EventId,
                runtimeEvent.ProviderSessionId);
            return AgentRuntimeEventResult.SessionNotFound;
        }

        if (!string.IsNullOrWhiteSpace(runtimeEvent.ConversationId))
        {
            session.ConversationId = runtimeEvent.ConversationId;
        }

        if (!string.IsNullOrWhiteSpace(runtimeEvent.TranscriptPath))
        {
            session.LastTranscriptPath = runtimeEvent.TranscriptPath;
        }

        session.RecentEvents.Add(runtimeEvent);
        if (session.RecentEvents.Count > 50)
        {
            session.RecentEvents.RemoveRange(0, session.RecentEvents.Count - 50);
        }

        session.State = runtimeEvent.EventType switch
        {
            "conversation.started" when session.State is ProviderSessionState.Created or ProviderSessionState.Starting => ProviderSessionState.Ready,
            "invocation.completed" when session.State == ProviderSessionState.Generating => ProviderSessionState.WaitingForRequest,
            "conversation.stopped" => ProviderSessionState.Stopped,
            "agy.exited" => ProviderSessionState.Stopped,
            _ => session.State
        };
        session.UpdatedAt = _timeProvider.GetUtcNow();

        _logger.LogInformation(
            "Accepted AGY event {EventType} for provider session {ProviderSessionId}",
            runtimeEvent.EventType,
            session.SessionId);
        return AgentRuntimeEventResult.Accepted;
    }

    public async Task<TranscriptDeltaReadResult> ReadTranscriptDeltaAsync(
        ProviderSession session,
        CancellationToken cancellationToken = default)
    {
        return await _transcriptDeltaReader.ReadDeltaAsync(session, cancellationToken);
    }

    private ProviderSession? GetByEvent(AgentRuntimeEvent runtimeEvent)
    {
        if (_sessions.TryGetValue(runtimeEvent.ProviderSessionId, out var session))
        {
            return session;
        }

        if (string.IsNullOrWhiteSpace(runtimeEvent.ConversationId))
        {
            return null;
        }

        return _sessions.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.ConversationId, runtimeEvent.ConversationId, StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateApiKey()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "wta_" + Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

public sealed record CreateProviderSessionResult(
    ProviderSession Session,
    string PlaintextApiKey,
    AgyRuntimeProbeResult RuntimeProbe);

public enum AgentRuntimeEventResult
{
    Accepted,
    Duplicate,
    SessionNotFound
}
