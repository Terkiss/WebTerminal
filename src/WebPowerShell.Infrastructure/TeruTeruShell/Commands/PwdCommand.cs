using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.TeruTeruShell.Commands;

public class PwdCommand : IShellCommand
{
    public string Name => "pwd";

    public async Task ExecuteAsync(string[] args, ICommandContext context)
    {
        await context.WriteLineAsync(context.FileSystem.CurrentDirectory);
    }
}
