namespace WebPowerShell.Infrastructure.AgentRuntime;

public sealed record AgentRuntimeEvent(
    string EventId,
    string EventType,
    Guid ProviderSessionId,
    string? ConversationId,
    int? StepIdx,
    string? TranscriptPath,
    DateTimeOffset Timestamp);
