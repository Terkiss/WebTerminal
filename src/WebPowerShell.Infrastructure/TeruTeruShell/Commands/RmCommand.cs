using System;
using System.IO;
using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.TeruTeruShell.Commands;

public class RmCommand : IShellCommand
{
    public string Name => "rm";

    public async Task ExecuteAsync(string[] args, ICommandContext context)
    {
        if (args.Length == 0)
        {
            await context.WriteErrorAsync("rm: missing operand");
            return;
        }

        bool recursive = false;
        int startIndex = 0;

        if (args[0] == "-r" || args[0] == "-rf")
        {
            recursive = true;
            startIndex = 1;
            if (args.Length == 1)
            {
                await context.WriteErrorAsync("rm: missing operand");
                return;
            }
        }

        var rootResult = context.FileSystem.ResolvePhysicalPath("/");

        for (int i = startIndex; i < args.Length; i++)
        {
            string targetPath = args[i];
            var resolvedResult = context.FileSystem.ResolvePhysicalPath(targetPath);
            if (!resolvedResult.IsSuccess)
            {
                await context.WriteErrorAsync($"rm: cannot remove '{targetPath}': {resolvedResult.Failure!.Message}");
                continue;
            }

            string physicalPath = resolvedResult.Value!;
            
            // Protect sandbox root
            if (rootResult.IsSuccess && physicalPath.Equals(rootResult.Value, StringComparison.OrdinalIgnoreCase))
            {
                await context.WriteErrorAsync($"rm: it is dangerous to operate recursively on '/' (sandbox root)");
                continue;
            }

            try
            {
                if (File.Exists(physicalPath))
                {
                    File.Delete(physicalPath);
                }
                else if (Directory.Exists(physicalPath))
                {
                    if (recursive)
                    {
                        Directory.Delete(physicalPath, true);
                    }
                    else
                    {
                        await context.WriteErrorAsync($"rm: cannot remove '{targetPath}': Is a directory (use -r to remove)");
                    }
                }
                else
                {
                    await context.WriteErrorAsync($"rm: cannot remove '{targetPath}': No such file or directory");
                }
            }
            catch (Exception ex)
            {
                await context.WriteErrorAsync($"rm: cannot remove '{targetPath}': {ex.Message}");
            }
        }
    }
}
