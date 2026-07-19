using System.Security.Cryptography;

namespace WebPowerShell.Infrastructure.AgentRuntime;

public sealed class ProviderSession
{
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public Guid OwnerUserId { get; init; }
    public string Profile { get; init; } = "agy-default";
    public string DisplayName { get; init; } = "AGY Provider Session";
    public ProviderSessionState State { get; set; } = ProviderSessionState.Created;
    public string? ConversationId { get; set; }
    public int? AgyProcessId { get; set; }
    public string? LastAgyLogPath { get; set; }
    public string? LastTranscriptPath { get; set; }
    public long TranscriptOffset { get; set; }
    public List<AgentRuntimeEvent> RecentEvents { get; } = [];
    public string ApiKeyHash { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddHours(8);
    public string? FailureReason { get; set; }
    public SemaphoreSlim RequestLock { get; } = new(1, 1);

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public static string HashApiKey(string apiKey)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(bytes);
    }
}
