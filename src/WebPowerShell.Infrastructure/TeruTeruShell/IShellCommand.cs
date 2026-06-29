using System.Threading;
using System.Threading.Tasks;
using WebPowerShell.Application.Common.Models;

namespace WebPowerShell.Infrastructure.TeruTeruShell;

public interface ICommandContext
{
    Task WriteOutputAsync(ShellOutputPayload payload);
    Task WriteLineAsync(string text, string color = "");
    Task WriteErrorAsync(string text);
    Task WriteSystemAsync(string text);
    CancellationToken CancellationToken { get; }
}

public interface IShellCommand
{
    string Name { get; }
    Task ExecuteAsync(string[] args, ICommandContext context);
}
