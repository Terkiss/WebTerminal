using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.TeruTeruShell.Commands;

public class DirCommand : IShellCommand
{
    public string Name => "dir";

    public async Task ExecuteAsync(string[] args, ICommandContext context)
    {
        string targetPath = args.FirstOrDefault() ?? ".";
        var resolvedResult = context.FileSystem.ResolvePhysicalPath(targetPath);

        if (!resolvedResult.IsSuccess)
        {
            await context.WriteErrorAsync($"dir: {targetPath}: {resolvedResult.Failure!.Message}");
            return;
        }

        string physicalPath = resolvedResult.Value!;

        if (Directory.Exists(physicalPath))
        {
            var directories = Directory.GetDirectories(physicalPath).Select(Path.GetFileName).OrderBy(n => n);
            var files = Directory.GetFiles(physicalPath).Select(Path.GetFileName).OrderBy(n => n);

            foreach (var dir in directories)
            {
                if (dir != null) await context.WriteLineAsync(dir + "/");
            }
            foreach (var file in files)
            {
                if (file != null) await context.WriteLineAsync(file);
            }
        }
        else if (File.Exists(physicalPath))
        {
            await context.WriteLineAsync(Path.GetFileName(physicalPath));
        }
        else
        {
            await context.WriteErrorAsync($"dir: cannot access '{targetPath}': No such file or directory");
        }
    }
}
