using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pty.Net;

namespace WebPowerShell.Infrastructure.ConPTY;

public sealed class PtyNetTerminalProcess : ITerminalProcess
{
    private IPtyConnection? _connection;
    private int? _exitCode;

    public bool HasExited => _connection == null || _exitCode.HasValue;
    public int? ExitCode => _exitCode ?? (_connection?.ExitCode);

    public async Task StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken)
    {
        var ptyOptions = new PtyOptions
        {
            App = options.Executable,
            CommandLine = string.IsNullOrWhiteSpace(options.Arguments) ? Array.Empty<string>() : SplitCommandLine(options.Arguments),
            Cwd = options.WorkingDirectory,
            Cols = options.Columns,
            Rows = options.Rows,
            ForceWinPty = true,
            Environment = options.Environment == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(options.Environment)
        };

        _connection = await PtyProvider.SpawnAsync(ptyOptions, cancellationToken);
        _connection.ProcessExited += (_, args) => _exitCode = args.ExitCode;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
    {
        if (_connection == null) return;

        await _connection.WriterStream.WriteAsync(input, cancellationToken);
        await _connection.WriterStream.FlushAsync(cancellationToken);
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        _connection?.Resize(cols: columns, rows: rows);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadOutputAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_connection == null) yield break;

        var buffer = new byte[16 * 1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = await _connection.ReaderStream.ReadAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch
            {
                yield break;
            }

            if (bytesRead == 0) yield break;

            yield return new ReadOnlyMemory<byte>(buffer, 0, bytesRead);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _connection?.Kill();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _connection?.Kill();
        (_connection as IDisposable)?.Dispose();
        _connection = null;
        return ValueTask.CompletedTask;
    }

    private static string[] SplitCommandLine(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var ch = commandLine[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args.ToArray();
    }
}
