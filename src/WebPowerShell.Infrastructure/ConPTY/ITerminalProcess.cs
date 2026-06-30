using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.ConPTY;

public sealed record TerminalLaunchOptions(
    string Executable,
    string Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment,
    int Columns,
    int Rows);

public interface ITerminalProcess : IAsyncDisposable
{
    Task StartAsync(
        TerminalLaunchOptions options,
        CancellationToken cancellationToken);

    ValueTask WriteAsync(
        ReadOnlyMemory<byte> input,
        CancellationToken cancellationToken);

    Task ResizeAsync(
        int columns,
        int rows,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadOutputAsync(
        CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    bool HasExited { get; }
    int? ExitCode { get; }
}
