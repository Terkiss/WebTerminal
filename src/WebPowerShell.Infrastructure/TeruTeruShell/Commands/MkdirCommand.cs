using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.TeruTeruShell.Commands;

public class MkdirCommand : IShellCommand
{
    public string Name => "mkdir";

    public async Task ExecuteAsync(string[] args, ICommandContext context)
    {
        if (args.Length == 0)
        {
            await context.WriteErrorAsync("mkdir: missing operand");
            return;
        }

        foreach (var targetPath in args)
        {
            var resolvedResult = context.FileSystem.ResolvePhysicalPath(targetPath);
            if (!resolvedResult.IsSuccess)
            {
                await context.WriteErrorAsync($"mkdir: cannot create directory '{targetPath}': {resolvedResult.Failure!.Message}");
                continue;
            }

            try
            {
                Directory.CreateDirectory(resolvedResult.Value!);
            }
            catch (Exception ex)
            {
                await context.WriteErrorAsync($"mkdir: cannot create directory '{targetPath}': {ex.Message}");
            }
        }
    }
}
