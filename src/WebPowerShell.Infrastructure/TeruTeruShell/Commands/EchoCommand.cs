using System.Threading;
using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.TeruTeruShell.Commands;

public class EchoCommand : IShellCommand
{
    public string Name => "echo";

    public async Task ExecuteAsync(string[] args, ICommandContext context)
    {
        var text = string.Join(" ", args);
        await context.WriteLineAsync(text);
    }
}
