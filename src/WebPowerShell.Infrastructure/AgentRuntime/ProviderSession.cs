using System.Security.Cryptography;

namespace WebPowerShell.Infrastructure.AgentRuntime;

public sealed class ProviderSession
{
    private const int MaxRememberedRequestIds = 100;
    private readonly object _requestIdsLock = new();
    private readonly Queue<string> _acceptedRequestIds = new();
    private readonly HashSet<string> _acceptedRequestIdSet = new(StringComparer.Ordinal);

    public Guid SessionId { get; init; } = Guid.NewGuid();
    public Guid OwnerUserId { get; init; }
    public string Profile { get; init; } = "agy-default";
    public string DisplayName { get; init; } = "AGY Provider Session";
    public ProviderSessionState State { get; set; } = ProviderSessionState.Created;
    public string? ConversationId { get; set; }
    public int? AgyProcessId { get; set; }
    public Guid? TerminalSessionId { get; set; }
    public bool IsTerminalBacked { get; set; }
    public string? LastAgyLogPath { get; set; }
    public string? LastTranscriptPath { get; set; }
    public long TranscriptOffset { get; set; }
    public List<AgentRuntimeEvent> RecentEvents { get; } = [];
    public string ApiKeyHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddHours(8);
    public string? FailureReason { get; set; }
    public SemaphoreSlim RequestLock { get; } = new(1, 1);
    public HashSet<string> ExpectedToolCallIds { get; } = new(StringComparer.Ordinal);
    public string? LastResponseId { get; set; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool TryAcceptRequestId(string? requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return true;
        }

        var normalized = requestId.Trim();
        lock (_requestIdsLock)
        {
            if (!_acceptedRequestIdSet.Add(normalized))
            {
                return false;
            }

            _acceptedRequestIds.Enqueue(normalized);
            while (_acceptedRequestIds.Count > MaxRememberedRequestIds)
            {
                var expired = _acceptedRequestIds.Dequeue();
                _acceptedRequestIdSet.Remove(expired);
            }

            return true;
        }
    }

    public static string HashApiKey(string apiKey)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(bytes);
    }
}
