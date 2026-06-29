using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace WebPowerShell.Infrastructure.TeruTeruShell.Commands;

public class AgyCommand : IShellCommand
{
    public string Name => "agy";

    public async Task ExecuteAsync(string[] args, ICommandContext context)
    {
        Process? process = null;
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "agy",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true, // To close it
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                processStartInfo.ArgumentList.Add(arg);
            }

            var resolvedDirResult = context.FileSystem.ResolvePhysicalPath(context.FileSystem.CurrentDirectory);
            if (resolvedDirResult.IsSuccess)
            {
                processStartInfo.WorkingDirectory = resolvedDirResult.Value;
            }
            
            process = new Process { StartInfo = processStartInfo };
            process.Start();

            // Close standard input to prevent deadlock if process waits for input
            process.StandardInput.Close();

            var stdoutTask = ReadStreamAsync(process.StandardOutput, line => context.WriteLineAsync(line));
            var stderrTask = ReadStreamAsync(process.StandardError, line => context.WriteErrorAsync(line));

            using var ctr = context.CancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch { } // Ignore errors during kill
            });

            await process.WaitForExitAsync(context.CancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (OperationCanceledException)
        {
            await context.WriteLineAsync("agy: command canceled.");
        }
        catch (Exception ex)
        {
            await context.WriteErrorAsync($"agy: {ex.Message}");
        }
        finally
        {
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch { }
                process.Dispose();
            }
        }
    }

    private async Task ReadStreamAsync(StreamReader reader, Func<string, Task> onLineRead)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                await onLineRead(line);
            }
        }
        catch
        {
            // Ignore stream read errors on close
        }
    }
}
