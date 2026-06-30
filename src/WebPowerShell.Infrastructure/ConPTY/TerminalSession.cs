using System;
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
    public string? ConnectionId { get; set; }

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
        _ = Process.StartAsync(options, _cts.Token).ContinueWith(t => {
            if (t.IsFaulted) _logger.LogError(t.Exception, "Failed to start terminal process");
            else {
                _inputPumpTask = RunInputPumpAsync();
                _outputPumpTask = RunOutputPumpAsync();
            }
        });
    }

    public void Attach(string connectionId)
    {
        ConnectionId = connectionId;
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public void Detach()
    {
        ConnectionId = null;
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
                if (OnOutput != null && ConnectionId != null)
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
