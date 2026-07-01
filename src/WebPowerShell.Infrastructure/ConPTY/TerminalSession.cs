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
    /// Scrollback buffer: captures all terminal output for replay on device attach/restore.
    /// Max 256KB to prevent unbounded memory growth.
    /// </summary>
    private const int MaxScrollbackBytes = 256 * 1024;
    private readonly object _scrollbackLock = new();
    private byte[] _scrollbackBuffer = new byte[MaxScrollbackBytes];
    private int _scrollbackLength = 0;
    private bool _scrollbackWrapped = false;

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
                var data = chunk.ToArray();

                // Always capture output to scrollback buffer (even if no clients connected)
                AppendToScrollback(data);

                if (OnOutput != null && HasConnections)
                {
                    await OnOutput(data);
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

    /// <summary>
    /// Append output data to the circular scrollback buffer.
    /// </summary>
    private void AppendToScrollback(byte[] data)
    {
        lock (_scrollbackLock)
        {
            if (data.Length >= MaxScrollbackBytes)
            {
                // Data is larger than buffer — keep only the last MaxScrollbackBytes
                Array.Copy(data, data.Length - MaxScrollbackBytes, _scrollbackBuffer, 0, MaxScrollbackBytes);
                _scrollbackLength = MaxScrollbackBytes;
                _scrollbackWrapped = true;
                return;
            }

            var spaceRemaining = MaxScrollbackBytes - _scrollbackLength;
            if (data.Length <= spaceRemaining)
            {
                // Fits in remaining space
                Array.Copy(data, 0, _scrollbackBuffer, _scrollbackLength, data.Length);
                _scrollbackLength += data.Length;
            }
            else
            {
                // Shift left and append at end (ring-style)
                var shiftBy = data.Length - spaceRemaining;
                Array.Copy(_scrollbackBuffer, shiftBy, _scrollbackBuffer, 0, _scrollbackLength - shiftBy);
                _scrollbackLength -= shiftBy;
                Array.Copy(data, 0, _scrollbackBuffer, _scrollbackLength, data.Length);
                _scrollbackLength += data.Length;
                _scrollbackWrapped = true;
            }
        }
    }

    /// <summary>
    /// Get a snapshot of the scrollback buffer for replay to a newly attached client.
    /// </summary>
    public byte[] GetScrollbackSnapshot()
    {
        lock (_scrollbackLock)
        {
            var result = new byte[_scrollbackLength];
            Array.Copy(_scrollbackBuffer, 0, result, 0, _scrollbackLength);
            return result;
        }
    }

    /// <summary>
    /// Load scrollback buffer from persisted data (called on session restore).
    /// </summary>
    public void LoadScrollback(byte[] data)
    {
        lock (_scrollbackLock)
        {
            var len = Math.Min(data.Length, MaxScrollbackBytes);
            if (len > 0)
            {
                Array.Copy(data, data.Length - len, _scrollbackBuffer, 0, len);
            }
            _scrollbackLength = len;
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
