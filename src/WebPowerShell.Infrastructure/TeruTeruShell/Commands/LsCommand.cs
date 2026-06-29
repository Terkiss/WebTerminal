using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.TeruTeruShell.Commands;

public class LsCommand : IShellCommand
{
    public string Name => "ls";

    public async Task ExecuteAsync(string[] args, ICommandContext context)
    {
        var dirCommand = new DirCommand();
        await dirCommand.ExecuteAsync(args, context);
    }
}
