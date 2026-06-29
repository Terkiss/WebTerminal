using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Infrastructure.PowerShell
{
    public class PowerShellSessionService : IPowerShellSessionService, IAsyncDisposable, IDisposable
    {
        private readonly ConcurrentDictionary<(Guid UserId, Guid TabId), SessionEntry> _sessions = new();
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PowerShellSessionService> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private bool _disposed;
        private readonly object _disposedLock = new();

        public PowerShellSessionService(TimeProvider timeProvider, ILogger<PowerShellSessionService> logger, ILoggerFactory loggerFactory)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public async Task<Result<PowerShellSession>> CreateSessionAsync(Guid userId, Guid tabId, Func<string, Task> onOutput, Func<string, Task> onError, Func<Task> onExited, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var key = (userId, tabId);
            
            if (_sessions.TryRemove(key, out var oldEntry))
            {
                await ReleaseSessionEntryAsync(oldEntry, cancellationToken);
            }

            try
            {
                var utcNow = _timeProvider.GetUtcNow();
                var sessionMetadata = new PowerShellSession
                {
                    SessionId = Guid.NewGuid(),
                    UserId = userId,
                    TabId = tabId,
                    CreatedAt = utcNow,
                    LastActiveAt = utcNow
                };

                var ptyLogger = _loggerFactory.CreateLogger<PtyProcessWrapper>();
                // `-NoLogo` helps reduce noise when terminal opens. We keep it standard.
                var pty = new PtyProcessWrapper(ptyLogger, "powershell.exe", "-NoLogo");

                pty.OutputDataReceived += async (s, e) => {
                    try { await onOutput(e); } catch (Exception ex) { _logger.LogError(ex, "Error in onOutput callback."); }
                };
                pty.ErrorDataReceived += async (s, e) => {
                    try { await onError(e); } catch (Exception ex) { _logger.LogError(ex, "Error in onError callback."); }
                };
                pty.ProcessExited += async (s, e) => {
                    try { await onExited(); } catch (Exception ex) { _logger.LogError(ex, "Error in onExited callback."); }
                };

                pty.Start();

                var entry = new SessionEntry
                {
                    SessionMetadata = sessionMetadata,
                    PtyProcess = pty
                };

                if (!_sessions.TryAdd(key, entry))
                {
                    _logger.LogWarning("Concurrent session creation conflict for UserId: {UserId}, TabId: {TabId}", userId, tabId);
                    await pty.DisposeAsync();
                    return Result<PowerShellSession>.Fail(new AppFailure("PtyCreationFailed", "동시 세션 생성 충돌이 발생했습니다."));
                }

                return Result<PowerShellSession>.Success(sessionMetadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create PtyProcess wrapper for UserId: {UserId}, TabId: {TabId}", userId, tabId);
                return Result<PowerShellSession>.Fail(new AppFailure("PtyCreationFailed", $"PtyProcess 생성에 실패했습니다: {ex.Message}"));
            }
        }

        public async Task<Result<bool>> WriteInputAsync(Guid userId, Guid tabId, string input, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var key = (userId, tabId);
            if (!_sessions.TryGetValue(key, out var entry))
            {
                return Result<bool>.Fail(AppFailure.SessionNotFound);
            }

            entry.SessionMetadata.LastActiveAt = _timeProvider.GetUtcNow();

            try
            {
                await entry.PtyProcess.WriteInputAsync(input, cancellationToken);
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing input to process for SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                return Result<bool>.Fail(new AppFailure("WriteInputFailed", $"입력 전달 중 오류가 발생했습니다: {ex.Message}"));
            }
        }

        public async Task<Result<bool>> StopCommandAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var key = (userId, tabId);
            if (!_sessions.TryGetValue(key, out var entry))
            {
                return Result<bool>.Fail(AppFailure.SessionNotFound);
            }

            entry.SessionMetadata.LastActiveAt = _timeProvider.GetUtcNow();

            try
            {
                // Send Ctrl+C
                await entry.PtyProcess.WriteControlCharacterAsync('\x03', cancellationToken);
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping command for SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                return Result<bool>.Fail(new AppFailure("StopCommandFailed", $"명령 중단 중 오류가 발생했습니다: {ex.Message}"));
            }
        }

        public async Task<Result<bool>> CloseSessionAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var key = (userId, tabId);
            if (!_sessions.TryRemove(key, out var entry))
            {
                return Result<bool>.Fail(AppFailure.SessionNotFound);
            }

            await ReleaseSessionEntryAsync(entry, cancellationToken);
            return Result<bool>.Success(true);
        }

        public Task<Result<PowerShellSession>> GetSessionAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var key = (userId, tabId);
            if (!_sessions.TryGetValue(key, out var entry))
            {
                return Task.FromResult(Result<PowerShellSession>.Fail(AppFailure.SessionNotFound));
            }

            return Task.FromResult(Result<PowerShellSession>.Success(entry.SessionMetadata));
        }

        public async Task<Result<int>> CloseAllSessionsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            int closedCount = 0;
            
            var keysToRemove = _sessions.Keys.Where(k => k.UserId == userId).ToList();

            foreach (var key in keysToRemove)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                if (_sessions.TryRemove(key, out var entry))
                {
                    await ReleaseSessionEntryAsync(entry, cancellationToken);
                    closedCount++;
                }
            }

            return Result<int>.Success(closedCount);
        }

        public async Task<Result<int>> CleanIdleSessionsAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var now = _timeProvider.GetUtcNow();
            var expiredSessions = new System.Collections.Generic.List<(Guid UserId, Guid TabId)>();

            foreach (var kvp in _sessions)
            {
                var entry = kvp.Value;
                if (now - entry.SessionMetadata.LastActiveAt >= idleTimeout)
                {
                    expiredSessions.Add(kvp.Key);
                }
            }

            int successCount = 0;
            foreach (var key in expiredSessions)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var result = await CloseSessionAsync(key.UserId, key.TabId, cancellationToken);
                if (result.IsSuccess)
                {
                    successCount++;
                }
            }

            return Result<int>.Success(successCount);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PowerShellSessionService));
            }
        }

        public async ValueTask DisposeAsync()
        {
            bool shouldDispose = false;
            lock (_disposedLock)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    shouldDispose = true;
                }
            }

            if (shouldDispose)
            {
                foreach (var key in _sessions.Keys)
                {
                    if (_sessions.TryRemove(key, out var entry))
                    {
                        await ReleaseSessionEntryAsync(entry);
                    }
                }
            }
        }

        public void Dispose()
        {
            bool shouldDispose = false;
            lock (_disposedLock)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    shouldDispose = true;
                }
            }

            if (shouldDispose)
            {
                foreach (var key in _sessions.Keys)
                {
                    if (_sessions.TryRemove(key, out var entry))
                    {
                        entry.PtyProcess.Dispose();
                    }
                }
            }
        }

        private async Task ReleaseSessionEntryAsync(SessionEntry entry, CancellationToken cancellationToken = default)
        {
            if (entry == null) return;

            try
            {
                await entry.PtyProcess.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing PtyProcess for SessionId: {SessionId}", entry.SessionMetadata.SessionId);
            }
        }

        private class SessionEntry
        {
            public required PowerShellSession SessionMetadata { get; init; }
            public required PtyProcessWrapper PtyProcess { get; init; }
        }
    }
}
