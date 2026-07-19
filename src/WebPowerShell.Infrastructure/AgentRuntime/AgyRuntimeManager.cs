using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
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
    private readonly AgyRuntimeOptions _options;

    public AgyRuntimeManager(
        AgyRuntimeProbe runtimeProbe,
        ILogger<AgyRuntimeManager> logger,
        IConfiguration configuration)
    {
        _runtimeProbe = runtimeProbe;
        _logger = logger;
        _options = LoadOptions(configuration);
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

            var logPath = Path.Combine(
                Path.GetTempPath(),
                $"webterminal-agy-{session.SessionId:N}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.log");
            foreach (var argument in BuildArguments(session, prompt, logPath, _options))
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            ApplyEnvironment(process.StartInfo, session, _options);
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

    internal static IReadOnlyList<string> BuildArguments(
        ProviderSession session,
        string prompt,
        string logPath,
        AgyRuntimeOptions options)
    {
        var arguments = new List<string>
        {
            "--print",
            prompt,
            "--print-timeout",
            string.IsNullOrWhiteSpace(options.PrintTimeout) ? "2m" : options.PrintTimeout,
            "--log-file",
            logPath
        };

        AddOption(arguments, "--agent", options.Agent);
        AddOption(arguments, "--model", options.Model);
        AddOption(arguments, "--mode", options.Mode);
        AddOption(arguments, "--project", options.Project);

        foreach (var workspacePath in options.WorkspacePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            arguments.Add("--add-dir");
            arguments.Add(workspacePath);
        }

        if (!string.IsNullOrWhiteSpace(session.ConversationId))
        {
            arguments.Add("--conversation");
            arguments.Add(session.ConversationId);
        }

        return arguments;
    }

    private static void ApplyEnvironment(ProcessStartInfo startInfo, ProviderSession session, AgyRuntimeOptions options)
    {
        startInfo.Environment["WEBTERMINAL_PROVIDER_SESSION_ID"] = session.SessionId.ToString();

        if (!string.IsNullOrWhiteSpace(options.InternalEventEndpoint))
        {
            startInfo.Environment["WEBTERMINAL_AGENT_EVENT_ENDPOINT"] = options.InternalEventEndpoint;
        }

        if (!string.IsNullOrWhiteSpace(options.InternalEventSecret))
        {
            startInfo.Environment["WEBTERMINAL_AGENT_EVENT_SECRET"] = options.InternalEventSecret;
        }
    }

    private static void AddOption(List<string> arguments, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        arguments.Add(name);
        arguments.Add(value);
    }

    private static AgyRuntimeOptions LoadOptions(IConfiguration configuration)
    {
        return new AgyRuntimeOptions
        {
            PrintTimeout = configuration["AgentRuntime:PrintTimeout"] ?? "2m",
            Agent = configuration["AgentRuntime:Agent"],
            Model = configuration["AgentRuntime:Model"],
            Mode = configuration["AgentRuntime:Mode"],
            Project = configuration["AgentRuntime:Project"],
            InternalEventEndpoint = configuration["AgentRuntime:InternalEventEndpoint"],
            InternalEventSecret = configuration["AgentRuntime:InternalEventSecret"],
            WorkspacePaths = configuration
                .GetSection("AgentRuntime:WorkspacePaths")
                .GetChildren()
                .Select(section => section.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray()
        };
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
