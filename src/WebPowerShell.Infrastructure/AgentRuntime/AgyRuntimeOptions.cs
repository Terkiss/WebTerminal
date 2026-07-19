namespace WebPowerShell.Infrastructure.AgentRuntime;

public sealed class AgyRuntimeOptions
{
    public string PrintTimeout { get; init; } = "2m";
    public string? Agent { get; init; }
    public string? Model { get; init; }
    public string? Mode { get; init; }
    public string? Project { get; init; }
    public string? InternalEventEndpoint { get; init; }
    public string? InternalEventSecret { get; init; }
    public IReadOnlyList<string> WorkspacePaths { get; init; } = [];
}
