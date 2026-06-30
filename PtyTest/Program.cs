
using System;
using System.Threading;
using System.Threading.Tasks;
using Pty.Net;

class Program {
    static async Task Main() {
        var options = new PtyOptions
        {
            App = "cmd.exe",
            Cols = 80,
            Rows = 24,
            Cwd = Environment.CurrentDirectory
        };
        using var pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);
        Console.WriteLine("Spawned PTY PID: " + pty.Pid);
        pty.Kill();
    }
}

