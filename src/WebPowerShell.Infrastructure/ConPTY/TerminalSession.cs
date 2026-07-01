using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebPowerShell.Domain.Common;

namespace WebPowerShell.Infrastructure.ConPTY;

public sealed class TerminalSession : IAsyncDisposable
{
    public Guid SessionId { get; }
    public Guid OwnerUserId { get; }
    public ITerminalProcess Process { get; }
    
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastActivityAt { get; set; }
    public string WorkingDirectory { get; private set; } = "";

    /// <summary>
    /// All SignalR connection IDs currently attached to this session (multi-device mirroring).
    /// Thread-safe via lock.
    /// </summary>
    private readonly HashSet<string> _connectionIds = new();
    private readonly object _connectionLock = new();

    /// <summary>
    /// Returns a snapshot of all currently attached connection IDs.
    /// </summary>
    public IReadOnlyList<string> ConnectionIds
    {
        get
        {
            lock (_connectionLock)
            {
                return _connectionIds.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Returns true if at least one client is attached.
    /// </summary>
    public bool HasConnections
    {
        get
        {
            lock (_connectionLock)
            {
                return _connectionIds.Count > 0;
            }
        }
    }

    // Kept for backward compatibility — returns the first connection or null
    public string? ConnectionId
    {
        get
        {
            lock (_connectionLock)
            {
                return _connectionIds.Count > 0 ? _connectionIds.First() : null;
            }
        }
    }

    private readonly Channel<byte[]> _inputChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger;
    private Task? _inputPumpTask;
    private Task? _outputPumpTask;

    public Func<byte[], Task>? OnOutput { get; set; }
    public Func<int?, Task>? OnExited { get; set; }

    public TerminalSession(Guid sessionId, Guid ownerUserId, ITerminalProcess process, ILogger logger)
    {
        SessionId = sessionId;
        OwnerUserId = ownerUserId;
        Process = process;
        _logger = logger;
        CreatedAt = DateTimeOffset.UtcNow;
        LastActivityAt = DateTimeOffset.UtcNow;

        _inputChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Start(TerminalLaunchOptions options)
    {
        WorkingDirectory = options.WorkingDirectory;
        _ = Process.StartAsync(options, _cts.Token).ContinueWith(t => {
            if (t.IsFaulted) _logger.LogError(t.Exception, "Failed to start terminal process");
            else {
                _inputPumpTask = RunInputPumpAsync();
                _outputPumpTask = RunOutputPumpAsync();
            }
        });
    }

    /// <summary>
    /// Attach a client connection to this session (multi-device support).
    /// Multiple connections can be attached simultaneously for tmux-style mirroring.
    /// </summary>
    public void Attach(string connectionId)
    {
        lock (_connectionLock)
        {
            _connectionIds.Add(connectionId);
        }
        LastActivityAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("Session {SessionId}: attached connection {ConnectionId} (total: {Count})",
            SessionId, connectionId, _connectionIds.Count);
    }

    /// <summary>
    /// Detach a specific client connection from this session.
    /// </summary>
    public void Detach(string connectionId)
    {
        lock (_connectionLock)
        {
            _connectionIds.Remove(connectionId);
        }
        LastActivityAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("Session {SessionId}: detached connection {ConnectionId} (remaining: {Count})",
            SessionId, connectionId, _connectionIds.Count);
    }

    /// <summary>
    /// Detach all connections (legacy behavior).
    /// </summary>
    public void DetachAll()
    {
        lock (_connectionLock)
        {
            _connectionIds.Clear();
        }
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public async Task SendInputAsync(byte[] input)
    {
        LastActivityAt = DateTimeOffset.UtcNow;
        await _inputChannel.Writer.WriteAsync(input, _cts.Token);
    }

    public async Task ResizeAsync(int columns, int rows)
    {
        LastActivityAt = DateTimeOffset.UtcNow;
        await Process.ResizeAsync(columns, rows, _cts.Token);
    }

    private async Task RunInputPumpAsync()
    {
        try
        {
            await foreach (var input in _inputChannel.Reader.ReadAllAsync(_cts.Token))
            {
                await Process.WriteAsync(input, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in InputPump");
        }
    }

    private async Task RunOutputPumpAsync()
    {
        try
        {
            await foreach (var chunk in Process.ReadOutputAsync(_cts.Token))
            {
                if (OnOutput != null && HasConnections)
                {
                    await OnOutput(chunk.ToArray());
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OutputPump");
        }
        finally
        {
            if (OnExited != null)
            {
                await OnExited(Process.ExitCode);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _inputChannel.Writer.TryComplete();
        await Process.DisposeAsync();
        
        if (_inputPumpTask != null) await Task.WhenAny(_inputPumpTask);
        if (_outputPumpTask != null) await Task.WhenAny(_outputPumpTask);
        
        _cts.Dispose();
    }
}
