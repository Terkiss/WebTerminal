using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Common.Models;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Infrastructure.TeruTeruShell;

public class TeruTeruEngine : ITeruTeruEngine
{
    private class SessionContext : ICommandContext, IDisposable
    {
        public Guid SessionId { get; }
        public Guid TabId { get; }
        public Guid UserId { get; }
        public Func<ShellOutputPayload, Task> OnOutput { get; }
        public Func<Task> OnExited { get; }
        private CancellationTokenSource _executionCts = new();
        private readonly SemaphoreSlim _executionLock = new(1, 1);
        public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.UtcNow;
        public CancellationToken CancellationToken => _executionCts.Token;
        public SemaphoreSlim ExecutionLock => _executionLock;
        public IVirtualFileSystem FileSystem { get; }

        public void CancelCurrentCommand()
        {
            _executionCts.Cancel();
        }

        public void ResetCancellationToken()
        {
            if (_executionCts.IsCancellationRequested)
            {
                _executionCts.Dispose();
                _executionCts = new CancellationTokenSource();
            }
        }

        public void Dispose()
        {
            if (!_executionCts.IsCancellationRequested)
            {
                _executionCts.Cancel();
            }
            _executionCts.Dispose();
            _executionLock.Dispose();
        }

        public SessionContext(Guid userId, Guid tabId, Func<ShellOutputPayload, Task> onOutput, Func<Task> onExited, IVirtualFileSystem fileSystem)
        {
            SessionId = Guid.NewGuid();
            UserId = userId;
            TabId = tabId;
            OnOutput = onOutput;
            OnExited = onExited;
            FileSystem = fileSystem;
        }

        public async Task WriteOutputAsync(ShellOutputPayload payload)
        {
            await OnOutput(payload);
        }

        public async Task WriteLineAsync(string text, string color = "")
        {
            await OnOutput(new ShellOutputPayload { Type = "stdout", Text = text + "\r\n", Color = color });
        }

        public async Task WriteErrorAsync(string text)
        {
            await OnOutput(new ShellOutputPayload { Type = "stderr", Text = text + "\r\n", Color = "red" });
        }

        public async Task WriteSystemAsync(string text)
        {
            await OnOutput(new ShellOutputPayload { Type = "system", Text = text + "\r\n", Color = "cyan" });
        }

        public async Task WritePromptAsync()
        {
            string dir = FileSystem.CurrentDirectory;
            if (string.IsNullOrEmpty(dir)) dir = "/";
            string prompt = $"\r\n\x1b[1;32mteru@shell \x1b[1;34m{dir}\x1b[1;32m> \x1b[0m";
            await OnOutput(new ShellOutputPayload { Type = "prompt", Text = prompt });
        }
    }

    private readonly ILogger<TeruTeruEngine> _logger;
    private readonly ShellParser _parser;
    private readonly ConcurrentDictionary<string, SessionContext> _sessions = new();
    private readonly Dictionary<string, IShellCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider _serviceProvider;

    public TeruTeruEngine(ILogger<TeruTeruEngine> logger, IEnumerable<IShellCommand> commands, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _parser = new ShellParser();
        foreach (var cmd in commands)
        {
            _commands[cmd.Name] = cmd;
        }
    }

    private string GetSessionKey(Guid userId, Guid tabId) => $"{userId}:{tabId}";

    public async Task<Result<PowerShellSession>> CreateSessionAsync(Guid userId, Guid tabId, Func<ShellOutputPayload, Task> onOutput, Func<Task> onExited, CancellationToken cancellationToken = default)
    {
        var key = GetSessionKey(userId, tabId);
        
        using var scope = _serviceProvider.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var userResult = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (!userResult.IsSuccess)
        {
            return Result<PowerShellSession>.Fail(AppFailure.Unauthorized);
        }

        var user = userResult.Value!;
        bool isAdmin = user.IsAdmin;
        var fileSystem = new VirtualFileSystem(isAdmin, user.Username);

        var context = new SessionContext(userId, tabId, onOutput, onExited, fileSystem);
        _sessions[key] = context;

        var session = new PowerShellSession
        {
            SessionId = context.SessionId,
            TabId = tabId,
            UserId = userId,
            CreatedAt = context.LastActiveAt,
            LastActiveAt = context.LastActiveAt
        };

        _logger.LogInformation("Created TeruTeruSession {SessionId} for User {UserId} Tab {TabId}", session.SessionId, userId, tabId);
        
        await context.WriteSystemAsync("Welcome to TeruTeruShell!");
        await context.WritePromptAsync();
        
        return Result<PowerShellSession>.Success(session);
    }

    public async Task<Result<bool>> ExecuteCommandAsync(Guid userId, Guid tabId, string command, CancellationToken cancellationToken = default)
    {
        var key = GetSessionKey(userId, tabId);
        if (!_sessions.TryGetValue(key, out var context))
        {
            return Result<bool>.Fail(AppFailure.SessionNotFound);
        }

        context.LastActiveAt = DateTimeOffset.UtcNow;

        if (!await context.ExecutionLock.WaitAsync(0))
        {
            await context.WriteErrorAsync("Another command is already running.");
            return Result<bool>.Success(true);
        }

        try
        {
            context.ResetCancellationToken();
            var parsed = _parser.Parse(command);
            if (string.IsNullOrEmpty(parsed.CommandName))
            {
                await context.WritePromptAsync();
                return Result<bool>.Success(true);
            }

            await context.WriteSystemAsync($"$ {command}");

            try
            {
                if (_commands.TryGetValue(parsed.CommandName, out var cmd))
                {
                    await cmd.ExecuteAsync(parsed.Args, context);
                }
                else if (parsed.CommandName.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    var available = string.Join(", ", _commands.Keys.Concat(new[] { "help", "clear" }).OrderBy(x => x));
                    await context.WriteLineAsync($"Available commands: {available}");
                }
                else if (parsed.CommandName.Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    await context.WriteOutputAsync(new ShellOutputPayload { Type = "system", Text = "CLEAR" });
                }
                else
                {
                    await context.WriteErrorAsync($"Command not found: {parsed.CommandName}");
                }
                
                await context.WritePromptAsync();
                return Result<bool>.Success(true);
            }
            catch (OperationCanceledException)
            {
                await context.WriteErrorAsync("Command execution stopped.");
                await context.WritePromptAsync();
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command");
                await context.WriteErrorAsync(ex.Message);
                await context.WritePromptAsync();
                return Result<bool>.Fail(AppFailure.InternalError);
            }
        }
        finally
        {
            context.ExecutionLock.Release();
        }
    }

    public Task<Result<bool>> StopCommandAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default)
    {
        var key = GetSessionKey(userId, tabId);
        if (_sessions.TryGetValue(key, out var context))
        {
            context.CancelCurrentCommand();
        }
        return Task.FromResult(Result<bool>.Success(true));
    }

    public async Task<Result<bool>> CloseSessionAsync(Guid userId, Guid tabId, CancellationToken cancellationToken = default)
    {
        var key = GetSessionKey(userId, tabId);
        if (_sessions.TryRemove(key, out var context))
        {
            context.CancelCurrentCommand();
            await context.OnExited();
            context.Dispose();
            return Result<bool>.Success(true);
        }
        return Result<bool>.Fail(AppFailure.SessionNotFound);
    }

    public async Task<Result<int>> CloseAllSessionsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        int count = 0;
        var keysToRemove = _sessions.Keys.Where(k => k.StartsWith($"{userId}:")).ToList();
        
        foreach (var key in keysToRemove)
        {
            if (_sessions.TryRemove(key, out var context))
            {
                context.CancelCurrentCommand();
                await context.OnExited();
                context.Dispose();
                count++;
            }
        }
        
        return Result<int>.Success(count);
    }

    public void Dispose()
    {
        foreach (var context in _sessions.Values)
        {
            context.Dispose();
        }
        _sessions.Clear();
    }
}
