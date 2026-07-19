namespace WebPowerShell.Infrastructure.AgentRuntime;

public enum ProviderSessionState
{
    Created,
    Starting,
    Ready,
    WaitingForRequest,
    Generating,
    WaitingForToolResult,
    Completed,
    Stopping,
    Stopped,
    Failed
}
