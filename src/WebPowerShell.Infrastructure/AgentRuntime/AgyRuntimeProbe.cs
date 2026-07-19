using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WebPowerShell.Infrastructure.AgentRuntime;

public sealed class AgyRuntimeProbe
{
    private readonly ILogger<AgyRuntimeProbe> _logger;

    public AgyRuntimeProbe(ILogger<AgyRuntimeProbe> logger)
    {
        _logger = logger;
    }

    public async Task<AgyRuntimeProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var executable = OperatingSystem.IsWindows() ? "agy.cmd" : "agy";
        var path = await TryResolveExecutableAsync(executable, cancellationToken);
        if (path == null && OperatingSystem.IsWindows())
        {
            path = await TryResolveExecutableAsync("agy.exe", cancellationToken);
        }

        if (path == null)
        {
            return new AgyRuntimeProbeResult(false, null, "AGY executable was not found on PATH.");
        }

        return new AgyRuntimeProbeResult(true, path, null);
    }

    private async Task<string?> TryResolveExecutableAsync(string executable, CancellationToken cancellationToken)
    {
        var lookup = OperatingSystem.IsWindows() ? "where" : "which";
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = lookup,
                ArgumentList = { executable },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return null;
            }

            return output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve AGY executable {Executable}", executable);
            return null;
        }
    }
}

public sealed record AgyRuntimeProbeResult(bool IsAvailable, string? ExecutablePath, string? ErrorMessage);
