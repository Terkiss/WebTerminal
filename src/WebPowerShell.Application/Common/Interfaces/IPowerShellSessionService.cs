using System;
using System.Threading;
using System.Threading.Tasks;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Application.Common.Interfaces
{
    public interface IPowerShellSessionService
    {
        Task<Result<PowerShellSession>> CreateSessionAsync(Guid userId, Guid tabId, Func<string, Task> onOutput, Func<string, Task> onError, Func<Task> onExited, CancellationToken cancellationToken = default);
        Task<Result<bool>> WriteInputAsync(Guid userId, Guid tabId, string input, CancellationToken cancellationToken = default);
        Task<Result<bool>> StopCommandAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default);
        Task<Result<bool>> CloseSessionAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default);
        Task<Result<PowerShellSession>> GetSessionAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default);
        Task<Result<int>> CleanIdleSessionsAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default);
        Task<Result<int>> CloseAllSessionsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
