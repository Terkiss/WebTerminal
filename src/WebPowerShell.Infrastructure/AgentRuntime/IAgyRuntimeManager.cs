namespace WebPowerShell.Infrastructure.AgentRuntime;

public interface IAgyRuntimeManager
{
    Task<ProviderSessionState> PrepareSessionAsync(
        ProviderSession session,
        CancellationToken cancellationToken = default);

    Task<AgyCompletionResult> CompleteAsync(
        ProviderSession session,
        string prompt,
        CancellationToken cancellationToken = default);
}
