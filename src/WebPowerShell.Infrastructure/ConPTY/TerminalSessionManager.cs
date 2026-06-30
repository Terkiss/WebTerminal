using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Common.Models;
using WebPowerShell.Domain.Common;

namespace WebPowerShell.Infrastructure.ConPTY;

public class TerminalSessionManager : ITerminalSessionManager
{
    private readonly ConcurrentDictionary<Guid, TerminalSession> _sessions = new();
    private readonly ILogger<TerminalSessionManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _cleanupTask;

    public TerminalSessionManager(ILogger<TerminalSessionManager> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        
        // Background task to clean up detached sessions after a grace period (e.g., 60 seconds)
        _cleanupTask = CleanupStaleSessionsAsync();
    }

    public Result<TerminalSession> GetSession(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Result<TerminalSession>.Success(session);
        }
        return Result<TerminalSession>.Fail(AppFailure.SessionNotFound);
    }

    public async Task<Result<TerminalSession>> CreateSessionAsync(Guid userId, Guid sessionId, TerminalLaunchOptions options)
    {
        var process = new WindowsConPtyProcess();
        var session = new TerminalSession(sessionId, userId, process, _logger);
        
        if (!_sessions.TryAdd(sessionId, session))
        {
            await session.DisposeAsync();
            return Result<TerminalSession>.Fail(new AppFailure("SessionAlreadyExists", "A session with this ID already exists."));
        }

        session.Start(options);
        return Result<TerminalSession>.Success(session);
    }

    public async Task<Result<bool>> CloseSessionAsync(Guid sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await session.DisposeAsync();
            return Result<bool>.Success(true);
        }
        return Result<bool>.Fail(AppFailure.SessionNotFound);
    }

    public async Task<Result<int>> CloseAllSessionsForUserAsync(Guid userId)
    {
        int count = 0;
        var userSessions = _sessions.Values.Where(s => s.OwnerUserId == userId).ToList();
        
        foreach (var session in userSessions)
        {
            if (_sessions.TryRemove(session.SessionId, out var removedSession))
            {
                await removedSession.DisposeAsync();
                count++;
            }
        }
        return Result<int>.Success(count);
    }

    private async Task CleanupStaleSessionsAsync()
    {
        var gracePeriod = TimeSpan.FromSeconds(60);
        
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), _cts.Token);
                
                var now = DateTimeOffset.UtcNow;
                var staleSessions = _sessions.Values
                    .Where(s => s.ConnectionId == null && (now - s.LastActivityAt) > gracePeriod)
                    .ToList();

                foreach (var session in staleSessions)
                {
                    _logger.LogInformation("Cleaning up stale detached session {SessionId}", session.SessionId);
                    await CloseSessionAsync(session.SessionId);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stale session cleanup");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _cleanupTask; } catch { }
        _cts.Dispose();

        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }
        _sessions.Clear();
    }
}
