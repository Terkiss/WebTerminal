using System;
using System.Threading;
using System.Threading.Tasks;
using WebPowerShell.Application.Sessions.Common;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Application.Common.Interfaces
{
    public interface IPowerShellSessionService
    {
        Task<Result<PowerShellSession>> CreateSessionAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default);
        Task<Result<bool>> ExecuteCommandAsync(Guid userId, Guid tabId, string command, Func<PowerShellStreamData, CancellationToken, Task> onStream, CancellationToken cancellationToken = default);
        Task<Result<bool>> StopCommandAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default);
        Task<Result<bool>> CloseSessionAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default);
        Task<Result<PowerShellSession>> GetSessionAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default);
    }
}
