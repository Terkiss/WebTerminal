using System;
using System.IO;
using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.TeruTeruShell.Commands;

public class CpCommand : IShellCommand
{
    public string Name => "cp";

    public async Task ExecuteAsync(string[] args, ICommandContext context)
    {
        if (args.Length < 2)
        {
            await context.WriteErrorAsync("cp: missing file operand");
            return;
        }

        string sourcePath = args[0];
        string destPath = args[1];

        var srcResult = context.FileSystem.ResolvePhysicalPath(sourcePath);
        if (!srcResult.IsSuccess)
        {
            await context.WriteErrorAsync($"cp: cannot stat '{sourcePath}': {srcResult.Failure!.Message}");
            return;
        }

        var destResult = context.FileSystem.ResolvePhysicalPath(destPath);
        if (!destResult.IsSuccess)
        {
            await context.WriteErrorAsync($"cp: cannot create regular file '{destPath}': {destResult.Failure!.Message}");
            return;
        }

        string srcPhysical = srcResult.Value!;
        string destPhysical = destResult.Value!;

        try
        {
            if (File.Exists(srcPhysical))
            {
                if (Directory.Exists(destPhysical))
                {
                    destPhysical = Path.Combine(destPhysical, Path.GetFileName(srcPhysical));
                }
                File.Copy(srcPhysical, destPhysical, true);
            }
            else if (Directory.Exists(srcPhysical))
            {
                await context.WriteErrorAsync($"cp: -r not specified; omitting directory '{sourcePath}'");
            }
            else
            {
                await context.WriteErrorAsync($"cp: cannot stat '{sourcePath}': No such file or directory");
            }
        }
        catch (Exception ex)
        {
            await context.WriteErrorAsync($"cp: error copying: {ex.Message}");
        }
    }
}
