using System;
using System.Threading;
using System.Threading.Tasks;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Application.Common.Models;

namespace WebPowerShell.Application.Common.Interfaces;

public interface ITeruTeruEngine : IDisposable
{
    Task<Result<PowerShellSession>> CreateSessionAsync(
        Guid userId, 
        Guid tabId, 
        Func<ShellOutputPayload, Task> onOutput, 
        Func<Task> onExited, 
        CancellationToken cancellationToken = default);
        
    Task<Result<bool>> ExecuteCommandAsync(
        Guid userId, 
        Guid tabId, 
        string command, 
        CancellationToken cancellationToken = default);
        
    Task<Result<bool>> StopCommandAsync(
        Guid userId, 
        Guid tabId, 
        CancellationToken cancellationToken = default);
        
    Task<Result<bool>> CloseSessionAsync(
        Guid userId, 
        Guid tabId, 
        CancellationToken cancellationToken = default);
        
    Task<Result<int>> CloseAllSessionsForUserAsync(
        Guid userId, 
        CancellationToken cancellationToken = default);
}
