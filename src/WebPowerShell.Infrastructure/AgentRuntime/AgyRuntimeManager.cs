using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace WebPowerShell.Infrastructure.AgentRuntime;

public sealed class AgyRuntimeManager : IAgyRuntimeManager
{
    private static readonly Regex CreatedConversationPattern = new(
        @"Created conversation (?<id>[0-9a-fA-F-]{36})",
        RegexOptions.Compiled);

    private static readonly Regex PrintConversationPattern = new(
        @"Print mode: conversation=(?<id>[0-9a-fA-F-]{36})",
        RegexOptions.Compiled);

    private readonly AgyRuntimeProbe _runtimeProbe;
    private readonly ILogger<AgyRuntimeManager> _logger;

    public AgyRuntimeManager(AgyRuntimeProbe runtimeProbe, ILogger<AgyRuntimeManager> logger)
    {
        _runtimeProbe = runtimeProbe;
        _logger = logger;
    }

    public async Task<ProviderSessionState> PrepareSessionAsync(
        ProviderSession session,
        CancellationToken cancellationToken = default)
    {
        session.State = ProviderSessionState.Starting;
        session.UpdatedAt = DateTimeOffset.UtcNow;

        var probe = await _runtimeProbe.ProbeAsync(cancellationToken);
        if (!probe.IsAvailable || probe.ExecutablePath == null)
        {
            session.State = ProviderSessionState.Failed;
            session.FailureReason = probe.ErrorMessage ?? "AGY executable is not available.";
            session.UpdatedAt = DateTimeOffset.UtcNow;
            return session.State;
        }

        session.State = ProviderSessionState.Ready;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        return session.State;
    }

    public async Task<AgyCompletionResult> CompleteAsync(
        ProviderSession session,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var probe = await _runtimeProbe.ProbeAsync(cancellationToken);
        if (!probe.IsAvailable || probe.ExecutablePath == null)
        {
            session.State = ProviderSessionState.Failed;
            session.FailureReason = probe.ErrorMessage ?? "AGY executable is not available.";
            session.UpdatedAt = DateTimeOffset.UtcNow;
            return AgyCompletionResult.Fail(session.FailureReason);
        }

        session.State = ProviderSessionState.Generating;
        session.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = probe.ExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("--print");
            process.StartInfo.ArgumentList.Add(prompt);
            process.StartInfo.ArgumentList.Add("--print-timeout");
            process.StartInfo.ArgumentList.Add("2m");
            if (!string.IsNullOrWhiteSpace(session.ConversationId))
            {
                process.StartInfo.ArgumentList.Add("--conversation");
                process.StartInfo.ArgumentList.Add(session.ConversationId);
            }

            var logPath = Path.Combine(
                Path.GetTempPath(),
                $"webterminal-agy-{session.SessionId:N}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.log");
            process.StartInfo.ArgumentList.Add("--log-file");
            process.StartInfo.ArgumentList.Add(logPath);
            session.LastAgyLogPath = logPath;

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null) stdout.AppendLine(args.Data);
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null) stderr.AppendLine(args.Data);
            };

            process.Start();
            session.AgyProcessId = process.Id;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                KillProcess(process);
                throw;
            }
            session.AgyProcessId = null;

            var output = stdout.ToString().Trim();
            var error = stderr.ToString().Trim();
            UpdateConversationIdFromLog(session, logPath);

            if (process.ExitCode != 0)
            {
                session.State = ProviderSessionState.Failed;
                session.FailureReason = string.IsNullOrWhiteSpace(error)
                    ? $"AGY exited with code {process.ExitCode}."
                    : error;
                session.UpdatedAt = DateTimeOffset.UtcNow;
                return AgyCompletionResult.Fail(session.FailureReason);
            }

            session.State = ProviderSessionState.WaitingForRequest;
            session.FailureReason = null;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            return AgyCompletionResult.Success(output);
        }
        catch (OperationCanceledException)
        {
            session.AgyProcessId = null;
            session.State = ProviderSessionState.Failed;
            session.FailureReason = "AGY completion was cancelled.";
            session.UpdatedAt = DateTimeOffset.UtcNow;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AGY completion failed for provider session {SessionId}", session.SessionId);
            session.State = ProviderSessionState.Failed;
            session.FailureReason = ex.Message;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            return AgyCompletionResult.Fail(ex.Message);
        }
    }

    private void UpdateConversationIdFromLog(ProviderSession session, string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
            {
                return;
            }

            foreach (var line in File.ReadLines(logPath))
            {
                var createdMatch = CreatedConversationPattern.Match(line);
                if (createdMatch.Success)
                {
                    session.ConversationId = createdMatch.Groups["id"].Value;
                    continue;
                }

                var printMatch = PrintConversationPattern.Match(line);
                if (printMatch.Success)
                {
                    session.ConversationId = printMatch.Groups["id"].Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse AGY log for provider session {SessionId}", session.SessionId);
        }
    }

    private void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to kill cancelled AGY process.");
        }
    }
}

public sealed record AgyCompletionResult(bool IsSuccess, string? Text, string? ErrorMessage)
{
    public static AgyCompletionResult Success(string text) => new(true, text, null);
    public static AgyCompletionResult Fail(string errorMessage) => new(false, null, errorMessage);
}
