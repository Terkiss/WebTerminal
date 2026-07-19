namespace WebPowerShell.Domain.Entities;

public class ProviderSessionRecord
{
    public Guid SessionId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Profile { get; set; } = "agy-default";
    public string DisplayName { get; set; } = "AGY Provider Session";
    public string State { get; set; } = "Created";
    public string? ConversationId { get; set; }
    public string? LastAgyLogPath { get; set; }
    public string? LastTranscriptPath { get; set; }
    public long TranscriptOffset { get; set; }
    public string ApiKeyHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? FailureReason { get; set; }
}
