using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.TeruTeruShell.Commands;

public class PingCommand : IShellCommand
{
    public string Name => "ping";

    public async Task ExecuteAsync(string[] args, ICommandContext context)
    {
        if (args.Length == 0)
        {
            await context.WriteErrorAsync("ping: missing host operand");
            return;
        }

        string host = args[0];
        try
        {
            using var ping = new Ping();
            await context.WriteLineAsync($"PING {host}...");
            
            for (int i = 0; i < 4; i++)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    break;
                }

                PingReply reply = await ping.SendPingAsync(host, 2000);
                if (reply.Status == IPStatus.Success)
                {
                    await context.WriteLineAsync($"Reply from {reply.Address}: bytes={reply.Buffer.Length} time={reply.RoundtripTime}ms TTL={reply.Options?.Ttl}");
                }
                else
                {
                    await context.WriteLineAsync($"Request timed out or failed: {reply.Status}");
                }
                
                await Task.Delay(1000, context.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            await context.WriteErrorAsync($"ping: {host}: {ex.Message}");
        }
    }
}
