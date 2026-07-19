namespace WebPowerShell.Infrastructure.AgentRuntime;

public interface IProviderSessionStore
{
    IReadOnlyList<ProviderSession> LoadActive(DateTimeOffset now);
    void Save(ProviderSession session);
    void Delete(Guid sessionId);
}
