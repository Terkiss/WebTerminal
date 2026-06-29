using System.Linq;
using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.TeruTeruShell.Commands;

public class CdCommand : IShellCommand
{
    public string Name => "cd";

    public async Task ExecuteAsync(string[] args, ICommandContext context)
    {
        string targetPath = args.FirstOrDefault() ?? "/";
        var result = context.FileSystem.ChangeDirectory(targetPath);
        if (!result.IsSuccess)
        {
            await context.WriteErrorAsync($"cd: {targetPath}: {result.Failure!.Message}");
        }
    }
}
