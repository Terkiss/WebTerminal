using System;
using System.Threading.Tasks;
using WebPowerShell.Domain.Common;

namespace WebPowerShell.Infrastructure.ConPTY;

public interface ITerminalSessionManager : IAsyncDisposable
{
    Result<TerminalSession> GetSession(Guid sessionId);
    Task<Result<TerminalSession>> CreateSessionAsync(Guid userId, Guid sessionId, TerminalLaunchOptions options);
    Task<Result<bool>> CloseSessionAsync(Guid sessionId);
    Task<Result<int>> CloseAllSessionsForUserAsync(Guid userId);
    IReadOnlyList<TerminalSession> GetAllSessions();
    IReadOnlyList<TerminalSession> GetSessionsForUser(Guid userId);
}
