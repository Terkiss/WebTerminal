using System;
using System.Collections.Concurrent;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Sessions.Common;
using PowerShellStreamType = WebPowerShell.Application.Sessions.Common.PowerShellStreamType;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Infrastructure.PowerShell
{
    public class PowerShellSessionService : IPowerShellSessionService, IAsyncDisposable, IDisposable
    {
        private readonly ConcurrentDictionary<(Guid UserId, Guid TabId), SessionEntry> _sessions = new();
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PowerShellSessionService> _logger;
        private bool _disposed;
        private readonly object _disposedLock = new();

        public PowerShellSessionService(TimeProvider timeProvider, ILogger<PowerShellSessionService> logger)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<PowerShellSession>> CreateSessionAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var key = (userId, tabId);
            
            // 기존 세션이 있다면 캐시에서 안전하게 제거하고 리소스 해제
            if (_sessions.TryRemove(key, out var oldEntry))
            {
                await ReleaseSessionEntryAsync(oldEntry, cancellationToken);
            }

            Runspace? runspace = null;
            SemaphoreSlim? @lock = null;
            try
            {
                // Runspace 생성 및 오픈
                runspace = RunspaceFactory.CreateRunspace();
                runspace.Open();

                var utcNow = _timeProvider.GetUtcNow();
                var sessionMetadata = new PowerShellSession
                {
                    SessionId = Guid.NewGuid(),
                    UserId = userId,
                    TabId = tabId,
                    CreatedAt = utcNow,
                    LastActiveAt = utcNow
                };

                @lock = new SemaphoreSlim(1, 1);
                var entry = new SessionEntry
                {
                    SessionMetadata = sessionMetadata,
                    Runspace = runspace,
                    PowerShellInstance = null,
                    Lock = @lock
                };

                if (!_sessions.TryAdd(key, entry))
                {
                    _logger.LogWarning("세션 생성 도중 동시 생성 충돌로 인해 TryAdd에 실패했습니다. 생성된 리소스를 강제 회수합니다. UserId: {UserId}, TabId: {TabId}", userId, tabId);
                    
                    try { runspace.Close(); }
                    catch (Exception ex) { _logger.LogError(ex, "동시 생성 실패로 인한 Runspace Close 중 오류 발생. UserId: {UserId}, TabId: {TabId}", userId, tabId); }
                    
                    try { runspace.Dispose(); }
                    catch (Exception ex) { _logger.LogError(ex, "동시 생성 실패로 인한 Runspace Dispose 중 오류 발생. UserId: {UserId}, TabId: {TabId}", userId, tabId); }
                    
                    try { @lock.Dispose(); }
                    catch (Exception ex) { _logger.LogError(ex, "동시 생성 실패로 인한 Lock Dispose 중 오류 발생. UserId: {UserId}, TabId: {TabId}", userId, tabId); }

                    return Result<PowerShellSession>.Fail(new AppFailure("RunspaceCreationFailed", "동시 세션 생성 충돌이 발생했습니다."));
                }

                return Result<PowerShellSession>.Success(sessionMetadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Runspace 생성 혹은 오픈 중 예외가 발생했습니다. UserId: {UserId}, TabId: {TabId}", userId, tabId);
                
                if (runspace != null)
                {
                    try { runspace.Close(); } catch { }
                    try { runspace.Dispose(); } catch { }
                }
                if (@lock != null)
                {
                    try { @lock.Dispose(); } catch { }
                }

                return Result<PowerShellSession>.Fail(new AppFailure("RunspaceCreationFailed", $"Runspace 생성에 실패했습니다: {ex.Message}"));
            }
        }

        public async Task<Result<bool>> ExecuteCommandAsync(Guid userId, Guid tabId, string command, Func<PowerShellStreamData, CancellationToken, Task> onStream, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var key = (userId, tabId);
            if (!_sessions.TryGetValue(key, out var entry))
            {
                return Result<bool>.Fail(AppFailure.SessionNotFound);
            }

            var utcNow = _timeProvider.GetUtcNow();
            entry.SessionMetadata.LastActiveAt = utcNow;

            // 동시 실행 차단 락 획득 (WaitAsync(0)으로 대기시간 없이 즉시 실패하게 함)
            var hasLock = await entry.Lock.WaitAsync(0, cancellationToken);
            if (!hasLock)
            {
                return Result<bool>.Fail(AppFailure.SessionBusy);
            }

            var channel = Channel.CreateUnbounded<PowerShellStreamData>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            var consumerTask = Task.Run(async () =>
            {
                await foreach (var data in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        await onStream(data, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "onStream callback error");
                    }
                }
            });

            System.Management.Automation.PowerShell ps = System.Management.Automation.PowerShell.Create();
            try
            {
                ps.Runspace = entry.Runspace;
                entry.PowerShellInstance = ps;
                ps.AddScript(command);

                var output = new PSDataCollection<PSObject>();

                var sessionId = entry.SessionMetadata.SessionId;
                RegisterStreamHandler(output, channel.Writer, PowerShellStreamType.Output, sessionId);
                RegisterStreamHandler(ps.Streams.Error, channel.Writer, PowerShellStreamType.Error, sessionId);
                RegisterStreamHandler(ps.Streams.Warning, channel.Writer, PowerShellStreamType.Warning, sessionId);
                RegisterStreamHandler(ps.Streams.Verbose, channel.Writer, PowerShellStreamType.Verbose, sessionId);
                RegisterStreamHandler(ps.Streams.Debug, channel.Writer, PowerShellStreamType.Debug, sessionId);
                RegisterStreamHandler(ps.Streams.Information, channel.Writer, PowerShellStreamType.Information, sessionId);

                var settings = new PSInvocationSettings();
                await ps.InvokeAsync<PSObject, PSObject>(null, output, settings, null, null);

                if (ps.HadErrors)
                {
                    return Result<bool>.Success(false);
                }

                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "명령어 실행 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                return Result<bool>.Fail(new AppFailure("CommandExecutionFailed", $"명령어 실행 중 오류가 발생했습니다: {ex.Message}"));
            }
            finally
            {
                channel.Writer.Complete();
                try
                {
                    await consumerTask;
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogInformation(ex, "consumerTask 대기 중 취소가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "consumerTask 대기 중 오류 발생. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                }

                entry.PowerShellInstance = null;
                try
                {
                    ps.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "명령어 실행 후 PowerShellInstance Dispose 중 오류 발생. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                }

                try
                {
                    entry.Lock.Release();
                }
                catch (ObjectDisposedException)
                {
                    // 락이 이미 Dispose된 경우 안전하게 무시
                }
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

            var utcNow = _timeProvider.GetUtcNow();
            entry.SessionMetadata.LastActiveAt = utcNow;

            try
            {
                var ps = entry.PowerShellInstance;
                if (ps != null)
                {
                    // 비동기적으로 중단 작업을 태스크로 실행하여 대기
                    await Task.Run(() => ps.Stop(), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PowerShell 인스턴스를 중단하는 중 예외가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
            }

            return Result<bool>.Success(true);
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
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

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
                        ReleaseSessionEntry(entry);
                    }
                }
            }
        }

        private void ReleaseSessionEntry(SessionEntry entry)
        {
            if (entry == null) return;

            // 1. 실행 중인 PowerShell 인스턴스 정지
            var ps = entry.PowerShellInstance;
            if (ps != null)
            {
                try
                {
                    ps.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PowerShell 인스턴스를 정지하는 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                }
            }

            // 2. entry.Lock.Wait()를 통해 현재 진행 중인 명령어 실행이 끝나서 락을 획득할 때까지 대기
            bool lockAcquired = false;
            try
            {
                // 타임아웃 5초 대기
                lockAcquired = entry.Lock.Wait(TimeSpan.FromSeconds(5));
                if (!lockAcquired)
                {
                    _logger.LogWarning("세션 정리 중 락을 획득하는 데 실패했습니다(타임아웃). 락 획득 없이 강제 정리를 진행합니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "세션 정리 중 락 대기가 취소되었습니다. 락 획득 없이 강제 정리를 진행합니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "세션 정리 중 락 대기 과정에서 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
            }

            // 3. 안전하게 Runspace와 SemaphoreSlim 등의 리소스를 Dispose 및 Close
            try
            {
                var psToDispose = entry.PowerShellInstance;
                if (psToDispose != null)
                {
                    try
                    {
                        psToDispose.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "PowerShell 인스턴스를 Dispose하는 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                    }
                    entry.PowerShellInstance = null;
                }

                if (entry.Runspace != null)
                {
                    try
                    {
                        entry.Runspace.Close();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Runspace를 Close하는 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                    }

                    try
                    {
                        entry.Runspace.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Runspace를 Dispose하는 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                    }
                }
            }
            finally
            {
                try
                {
                    entry.Lock.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SemaphoreSlim Lock을 Dispose하는 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                }
            }
        }

        private async Task ReleaseSessionEntryAsync(SessionEntry entry, CancellationToken cancellationToken = default)
        {
            if (entry == null) return;

            // 1. 실행 중인 PowerShell 인스턴스 정지
            var ps = entry.PowerShellInstance;
            if (ps != null)
            {
                try
                {
                    ps.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PowerShell 인스턴스를 정지하는 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                }
            }

            // 2. entry.Lock.WaitAsync()를 통해 현재 진행 중인 명령어 실행이 끝나서 락을 획득할 때까지 대기
            bool lockAcquired = false;
            try
            {
                // 타임아웃 5초 대기
                lockAcquired = await entry.Lock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                if (!lockAcquired)
                {
                    _logger.LogWarning("세션 정리 중 락을 획득하는 데 실패했습니다(타임아웃). 락 획득 없이 강제 정리를 진행합니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "세션 정리 중 락 대기가 취소되었습니다. 락 획득 없이 강제 정리를 진행합니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "세션 정리 중 락 대기 과정에서 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
            }

            // 3. 안전하게 Runspace와 SemaphoreSlim 등의 리소스를 Dispose 및 Close
            try
            {
                var psToDispose = entry.PowerShellInstance;
                if (psToDispose != null)
                {
                    try
                    {
                        psToDispose.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "PowerShell 인스턴스를 Dispose하는 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                    }
                    entry.PowerShellInstance = null;
                }

                if (entry.Runspace != null)
                {
                    try
                    {
                        entry.Runspace.Close();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Runspace를 Close하는 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                    }

                    try
                    {
                        entry.Runspace.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Runspace를 Dispose하는 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                    }
                }
            }
            finally
            {
                try
                {
                    entry.Lock.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SemaphoreSlim Lock을 Dispose하는 중 오류가 발생했습니다. SessionId: {SessionId}", entry.SessionMetadata.SessionId);
                }
            }
        }

        private void RegisterStreamHandler<T>(
            PSDataCollection<T> collection,
            ChannelWriter<PowerShellStreamData> writer,
            PowerShellStreamType streamType,
            Guid sessionId)
        {
            collection.DataAdded += (sender, args) =>
            {
                try
                {
                    if (args.Index >= 0 && args.Index < collection.Count)
                    {
                        var item = collection[args.Index];
                        var text = item?.ToString();
                        if (text != null)
                        {
                            var data = new PowerShellStreamData
                            {
                                Type = streamType,
                                Content = text,
                                Timestamp = _timeProvider.GetUtcNow()
                            };
                            writer.TryWrite(data);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{StreamType} DataAdded 처리 중 오류 발생. SessionId: {SessionId}", streamType, sessionId);
                }
            };
        }

        private class SessionEntry
        {
            public required PowerShellSession SessionMetadata { get; init; }
            public required Runspace Runspace { get; init; }
            public System.Management.Automation.PowerShell? PowerShellInstance { get; set; }
            public required SemaphoreSlim Lock { get; init; }
        }
    }
}
