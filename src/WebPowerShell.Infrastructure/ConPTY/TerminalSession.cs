using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Text;
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
    private readonly byte[] _scrollbackBuffer = new byte[MaxScrollbackBytes];
    private int _scrollbackHead = 0;
    private int _scrollbackLength = 0;

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

    /// <summary>
    /// Returns true if at least one API output capture is currently active on this session.
    /// </summary>
    public bool HasActiveApiCaptures
    {
        get
        {
            lock (_apiCaptureLock)
            {
                return _apiCaptures.Count > 0;
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
    private readonly object _apiCaptureLock = new();
    private readonly List<ApiOutputCapture> _apiCaptures = [];

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

    public async Task EmitSystemOutputAsync(string text, bool recordInScrollback = true)
    {
        var data = Encoding.UTF8.GetBytes(text);
        if (recordInScrollback)
        {
            AppendToScrollback(data);
        }

        if (OnOutput != null && HasConnections)
        {
            await OnOutput(data);
        }
    }

    public async Task<string> SendApiPromptAndCaptureAsync(
        string prompt,
        TimeSpan idleTimeout,
        TimeSpan hardTimeout,
        CancellationToken cancellationToken)
    {
        var capture = new ApiOutputCapture(idleTimeout);
        lock (_apiCaptureLock)
        {
            _apiCaptures.Add(capture);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        timeoutCts.CancelAfter(hardTimeout);
        using var registration = timeoutCts.Token.Register(() => capture.TryCancel());

        try
        {
            var sanitized = SanitizeApiPrompt(prompt);
            var normalized = $"{sanitized}\r";
            await SendInputAsync(Encoding.UTF8.GetBytes(normalized));
            return await capture.Task.WaitAsync(timeoutCts.Token);
        }
        finally
        {
            lock (_apiCaptureLock)
            {
                _apiCaptures.Remove(capture);
            }

            capture.Dispose();
        }
    }

    private static string SanitizeApiPrompt(string prompt)
    {
        if (string.IsNullOrEmpty(prompt))
        {
            return string.Empty;
        }

        var trimmed = prompt.TrimEnd('\r', '\n');
        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (ch is '\n' or '\t')
            {
                sb.Append(ch);
                continue;
            }

            if (char.IsControl(ch) || (ch >= '\u0080' && ch <= '\u009f'))
            {
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
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
                LastActivityAt = DateTimeOffset.UtcNow;

                // Always capture output to scrollback buffer (even if no clients connected)
                AppendToScrollback(data);
                NotifyApiCaptures(data);

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

    private void NotifyApiCaptures(byte[] data)
    {
        ApiOutputCapture[] captures;
        lock (_apiCaptureLock)
        {
            if (_apiCaptures.Count == 0)
            {
                return;
            }

            captures = _apiCaptures.ToArray();
        }

        foreach (var capture in captures)
        {
            try
            {
                capture.Append(data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session {SessionId}: API output capture append failed", SessionId);
            }
        }
    }

    /// <summary>
    /// Append output data to the O(1) circular scrollback buffer.
    /// </summary>
    private void AppendToScrollback(byte[] data)
    {
        if (data.Length == 0)
        {
            return;
        }

        lock (_scrollbackLock)
        {
            if (data.Length >= MaxScrollbackBytes)
            {
                // Data is larger than buffer — keep only the last MaxScrollbackBytes
                Array.Copy(data, data.Length - MaxScrollbackBytes, _scrollbackBuffer, 0, MaxScrollbackBytes);
                _scrollbackHead = 0;
                _scrollbackLength = MaxScrollbackBytes;
                return;
            }

            int firstChunk = Math.Min(data.Length, MaxScrollbackBytes - _scrollbackHead);
            Array.Copy(data, 0, _scrollbackBuffer, _scrollbackHead, firstChunk);
            int secondChunk = data.Length - firstChunk;
            if (secondChunk > 0)
            {
                Array.Copy(data, firstChunk, _scrollbackBuffer, 0, secondChunk);
            }

            _scrollbackHead = (_scrollbackHead + data.Length) % MaxScrollbackBytes;
            _scrollbackLength = Math.Min(MaxScrollbackBytes, _scrollbackLength + data.Length);
        }
    }

    /// <summary>
    /// Get a snapshot of the scrollback buffer for replay to a newly attached client.
    /// </summary>
    public byte[] GetScrollbackSnapshot()
    {
        lock (_scrollbackLock)
        {
            int length = _scrollbackLength;
            if (length == 0)
            {
                return Array.Empty<byte>();
            }

            int startIndex = (_scrollbackHead - length + MaxScrollbackBytes) % MaxScrollbackBytes;
            if (length == MaxScrollbackBytes)
            {
                // Skip leading UTF-8 continuation bytes (10xxxxxx) if ring buffer wrapped mid-character
                int skipped = 0;
                while (skipped < 3 && skipped < length)
                {
                    byte b = _scrollbackBuffer[(startIndex + skipped) % MaxScrollbackBytes];
                    if ((b & 0xC0) != 0x80)
                    {
                        break;
                    }
                    skipped++;
                }

                startIndex = (startIndex + skipped) % MaxScrollbackBytes;
                length -= skipped;
            }

            var result = new byte[length];
            int firstCopy = Math.Min(length, MaxScrollbackBytes - startIndex);
            Array.Copy(_scrollbackBuffer, startIndex, result, 0, firstCopy);
            int secondCopy = length - firstCopy;
            if (secondCopy > 0)
            {
                Array.Copy(_scrollbackBuffer, 0, result, firstCopy, secondCopy);
            }

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
            var toCopy = Math.Min(data.Length, MaxScrollbackBytes);
            if (toCopy > 0)
            {
                Array.Copy(data, data.Length - toCopy, _scrollbackBuffer, 0, toCopy);
            }
            _scrollbackHead = toCopy % MaxScrollbackBytes;
            _scrollbackLength = toCopy;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        lock (_apiCaptureLock)
        {
            foreach (var capture in _apiCaptures)
            {
                capture.TryCancel();
                capture.Dispose();
            }

            _apiCaptures.Clear();
        }

        _inputChannel.Writer.TryComplete();
        await Process.DisposeAsync();
        
        if (_inputPumpTask != null) await Task.WhenAny(_inputPumpTask);
        if (_outputPumpTask != null) await Task.WhenAny(_outputPumpTask);
        
        _cts.Dispose();
    }

    private sealed class ApiOutputCapture : IDisposable
    {
        private readonly object _lock = new();
        private readonly Decoder _utf8Decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _buffer = new();
        private readonly TimeSpan _idleTimeout;
        private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Timer? _idleTimer;
        private bool _disposed;

        public ApiOutputCapture(TimeSpan idleTimeout)
        {
            _idleTimeout = idleTimeout;
        }

        public Task<string> Task => _completion.Task;

        public void Append(byte[] data)
        {
            if (data.Length == 0)
            {
                return;
            }

            lock (_lock)
            {
                if (_disposed || _completion.Task.IsCompleted)
                {
                    return;
                }

                var charCount = _utf8Decoder.GetCharCount(data, 0, data.Length, flush: false);
                if (charCount > 0)
                {
                    var chars = new char[charCount];
                    var written = _utf8Decoder.GetChars(data, 0, data.Length, chars, 0, flush: false);
                    _buffer.Append(chars, 0, written);
                }

                _idleTimer ??= new Timer(_ => Complete(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _idleTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
            }
        }

        public void TryCancel()
        {
            _completion.TrySetCanceled();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _idleTimer?.Dispose();
                _idleTimer = null;
            }
        }

        private void Complete()
        {
            string value;
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                var flushCount = _utf8Decoder.GetCharCount(Array.Empty<byte>(), 0, 0, flush: true);
                if (flushCount > 0)
                {
                    var flushChars = new char[flushCount];
                    var written = _utf8Decoder.GetChars(Array.Empty<byte>(), 0, 0, flushChars, 0, flush: true);
                    _buffer.Append(flushChars, 0, written);
                }

                value = _buffer.ToString().Trim();
            }

            _completion.TrySetResult(value);
        }
    }
}
