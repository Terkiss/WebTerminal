
using System;
using System.Threading.Tasks;
using Quick.PtyNet;

class Program {
    static async Task Main() {
        var ptyProvider = new PtyProvider();
        var options = new PtyOptions
        {
            App = "cmd.exe",
            Cols = 80,
            Rows = 24,
            Cwd = Environment.CurrentDirectory
        };
        var pty = await ptyProvider.SpawnAsync(options);
        Console.WriteLine("Spawned PTY PID: " + pty.Pid);
        pty.Kill();
    }
}

