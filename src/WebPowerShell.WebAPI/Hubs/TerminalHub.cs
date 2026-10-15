using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using WebPowerShell.Domain.Common;
using WebPowerShell.Infrastructure.ConPTY;
using WebPowerShell.Infrastructure.AgentRuntime;
using WebPowerShell.Infrastructure.Persistence;

namespace WebPowerShell.WebAPI.Hubs;

[Authorize]
public class TerminalHub : Hub
{
    private readonly ILogger<TerminalHub> _logger;
    private readonly ITerminalSessionManager _sessionManager;
    private readonly IHubContext<TerminalHub> _hubContext;
    private readonly MemoryPersistenceService _persistenceService;
    private readonly ProviderSessionRegistry _providerSessionRegistry;
    private static readonly ConcurrentDictionary<Guid, ApiServerLineState> ApiServerCommandBuffers = new();
    private const string ApiServerStartCommand = "apiServerStart";

    private sealed class ApiServerLineState
    {
        public StringBuilder Buffer { get; } = new();
        public int ForwardedChars { get; set; }
    }

    public TerminalHub(
        ILogger<TerminalHub> logger,
        ITerminalSessionManager sessionManager,
        IHubContext<TerminalHub> hubContext,
        MemoryPersistenceService persistenceService,
        ProviderSessionRegistry providerSessionRegistry)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _hubContext = hubContext;
        _persistenceService = persistenceService;
        _providerSessionRegistry = providerSessionRegistry;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier ?? "unknown";
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("User {UserId} connected with connection ID {ConnectionId}", userId, connectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userIdString = Context.UserIdentifier ?? "unknown";
        var connectionId = Context.ConnectionId;

        _logger.LogInformation(exception, "User {UserId} disconnected with connection ID {ConnectionId}", userIdString, connectionId);

        // Detach this specific connection from all sessions it was attached to.
        // We DO NOT close sessions — TerminalSessionManager cleans up after grace period.
        if (Guid.TryParse(userIdString, out var uid))
        {
            foreach (var session in _sessionManager.GetSessionsForUser(uid))
            {
                session.Detach(connectionId);
            }
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<HubResponse> CreateSession(Guid tabId)
    {
        if (!TryGetUserId(out var userId)) return HubResponse.Fail(AppFailure.Unauthorized);

        // Launch powershell.exe natively. ConPTY will automatically translate output to UTF-8.
        var username = Context.User?.Identity?.Name ?? "unknown";
        var isAdmin = Context.User?.IsInRole("Admin") == true;
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var initScript = System.IO.Path.Combine(baseDir, "init.ps1");
        var homeDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.CurrentDirectory, "home", username));

        if (!System.IO.Directory.Exists(homeDir)) System.IO.Directory.CreateDirectory(homeDir);

        var args = $"-NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -File \"{initScript}\" \"{homeDir}\"";
        if (isAdmin) args += " -IsAdmin";

        var options = new TerminalLaunchOptions(
            Executable: "powershell.exe",
            Arguments: args,
            WorkingDirectory: homeDir,
            Environment: null,
            Columns: 80,
            Rows: 24
        );

        var result = await _sessionManager.CreateSessionAsync(userId, tabId, options);
        if (!result.IsSuccess) return HubResponse.Fail(result.Failure!);
        
        var session = result.Value!;
        
        SetupSessionCallbacks(session, tabId);

        // Automatically attach
        session.Attach(Context.ConnectionId);

        var scrollback = session.GetScrollbackSnapshot();
        if (scrollback.Length > 0)
        {
            try
            {
                await Clients.Caller.SendAsync("TerminalOutput", tabId, scrollback);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send initial scrollback to client");
            }
        }

        return HubResponse.Ok();
    }

    public async Task<HubResponse> AttachSession(Guid tabId, bool replayScrollback = true)
    {
        if (!TryGetUserId(out var userId)) return HubResponse.Fail(AppFailure.Unauthorized);

        var result = _sessionManager.GetSession(tabId);
        if (!result.IsSuccess) return HubResponse.Fail(result.Failure!);
        
        var session = result.Value!;
        if (session.OwnerUserId != userId) return HubResponse.Fail(AppFailure.Unauthorized);

        session.Attach(Context.ConnectionId);

        if (replayScrollback)
        {
            // Replay scrollback buffer to the newly attached client so they see previous output
            var scrollback = session.GetScrollbackSnapshot();
            if (scrollback.Length > 0)
            {
                try
                {
                    await Clients.Caller.SendAsync("TerminalOutput", tabId, scrollback);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send scrollback to client");
                }
            }
        }

        return HubResponse.Ok();
    }
    
    public async Task<HubResponse> DetachSession(Guid tabId)
    {
        if (!TryGetUserId(out var userId)) return HubResponse.Fail(AppFailure.Unauthorized);

        var result = _sessionManager.GetSession(tabId);
        if (!result.IsSuccess) return HubResponse.Fail(result.Failure!);
        
        var session = result.Value!;
        if (session.OwnerUserId != userId) return HubResponse.Fail(AppFailure.Unauthorized);

        session.Detach(Context.ConnectionId);
        return HubResponse.Ok();
    }

    public async Task<HubResponse> SendInput(Guid tabId, string inputBase64)
    {
        if (!TryGetUserId(out var userId)) return HubResponse.Fail(AppFailure.Unauthorized);

        byte[] input;
        try
        {
            input = Convert.FromBase64String(inputBase64);
        }
        catch (FormatException)
        {
            _logger.LogWarning("SendInput: invalid base64 from client");
            return HubResponse.Fail(new AppFailure("InvalidInput", "Input must be a valid base64 string."));
        }

        var result = _sessionManager.GetSession(tabId);
        if (!result.IsSuccess) return HubResponse.Fail(result.Failure!);
        
        var session = result.Value!;
        if (session.OwnerUserId != userId) return HubResponse.Fail(AppFailure.Unauthorized);

        if (await TryHandleApiServerCommandAsync(userId, session, input))
        {
            return HubResponse.Ok();
        }

        return HubResponse.Ok();
    }

    public async Task<HubResponse> Resize(Guid tabId, int columns, int rows)
    {
        if (!TryGetUserId(out var userId)) return HubResponse.Fail(AppFailure.Unauthorized);

        var result = _sessionManager.GetSession(tabId);
        if (!result.IsSuccess) return HubResponse.Fail(result.Failure!);
        
        var session = result.Value!;
        if (session.OwnerUserId != userId) return HubResponse.Fail(AppFailure.Unauthorized);

        await session.ResizeAsync(columns, rows);
        return HubResponse.Ok();
    }

    public async Task<HubResponse> CloseSession(Guid tabId)
    {
        if (!TryGetUserId(out var userId)) return HubResponse.Fail(AppFailure.Unauthorized);

        var result = _sessionManager.GetSession(tabId);
        if (!result.IsSuccess) return HubResponse.Fail(result.Failure!);
        
        var session = result.Value!;
        if (session.OwnerUserId != userId) return HubResponse.Fail(AppFailure.Unauthorized);

        ApiServerCommandBuffers.TryRemove(tabId, out _);
        _persistenceService.RemovePersistedSession(tabId);

        var closeResult = await _sessionManager.CloseSessionAsync(tabId);
        if (!closeResult.IsSuccess) return HubResponse.Fail(closeResult.Failure!);

        return HubResponse.Ok();
    }

    private bool TryGetUserId(out Guid userId)
    {
        return Guid.TryParse(Context.UserIdentifier, out userId);
    }

    private const string BracketedPasteStart = "\u001b[200~";
    private const string BracketedPasteEnd = "\u001b[201~";

    private async Task<bool> TryHandleApiServerCommandAsync(Guid userId, TerminalSession session, byte[] input)
    {
        var text = Encoding.UTF8.GetString(input);
        if (string.IsNullOrEmpty(text))
        {
            await session.SendInputAsync(input);
            return true;
        }

        var state = ApiServerCommandBuffers.GetOrAdd(session.SessionId, _ => new ApiServerLineState());

        // Fast-path: no newline in this input chunk -> forward immediately with zero hold/delay
        if (text.IndexOfAny(['\r', '\n']) < 0)
        {
            lock (state)
            {
                foreach (var ch in text)
                {
                    if (ch is '\u0003' or '\u0015')
                    {
                        state.Buffer.Clear();
                        state.ForwardedChars = 0;
                        continue;
                    }

                    if (ch is '\b' or '\u007f')
                    {
                        if (state.Buffer.Length > 0) state.Buffer.Length--;
                        if (state.ForwardedChars > 0) state.ForwardedChars--;
                        continue;
                    }

                    state.Buffer.Append(ch);
                    state.ForwardedChars++;
                }
            }

            await session.SendInputAsync(input);
            return true;
        }

        var pendingFlush = new StringBuilder();

        foreach (var ch in text)
        {
            if (ch is '\r' or '\n')
            {
                string command;
                int forwardedChars;
                lock (state)
                {
                    command = NormalizeApiServerCommandCandidate(state.Buffer.ToString());
                    forwardedChars = state.ForwardedChars;
                    state.Buffer.Clear();
                    state.ForwardedChars = 0;
                }

                if (string.Equals(command, ApiServerStartCommand, StringComparison.OrdinalIgnoreCase))
                {
                    pendingFlush.Clear();

                    if (forwardedChars > 0)
                    {
                        await session.SendInputAsync(Encoding.UTF8.GetBytes(new string('\b', forwardedChars)));
                    }

                    if (Context.User?.IsInRole("Admin") != true)
                    {
                        await session.EmitSystemOutputAsync("\r\n[WebTerminal] apiServerStart requires an administrator account.\r\n");
                        continue;
                    }

                    await StartApiServerSessionAsync(userId, session);
                    continue;
                }

                pendingFlush.Append(ch);
                continue;
            }

            if (ch is '\u0003' or '\u0015')
            {
                lock (state)
                {
                    state.Buffer.Clear();
                    state.ForwardedChars = 0;
                }

                pendingFlush.Append(ch);
                continue;
            }

            if (ch is '\b' or '\u007f')
            {
                lock (state)
                {
                    if (state.Buffer.Length > 0) state.Buffer.Length--;
                    if (state.ForwardedChars > 0) state.ForwardedChars--;
                }

                pendingFlush.Append(ch);
                continue;
            }

            lock (state)
            {
                state.Buffer.Append(ch);
            }

            pendingFlush.Append(ch);
        }

        if (pendingFlush.Length > 0)
        {
            await session.SendInputAsync(Encoding.UTF8.GetBytes(pendingFlush.ToString()));
        }

        return true;
    }

    private static string NormalizeApiServerCommandCandidate(string value)
    {
        return value
            .Replace(BracketedPasteStart, string.Empty, StringComparison.Ordinal)
            .Replace(BracketedPasteEnd, string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private async Task StartApiServerSessionAsync(Guid userId, TerminalSession terminalSession)
    {
        var result = _providerSessionRegistry.CreateTerminalBacked(
            userId,
            terminalSession.SessionId,
            $"Terminal API Provider {terminalSession.SessionId:N}"[..30]);

        var origin = GetOrigin();
        var baseUrl = $"{origin}/v1";
        var message = $"""

[WebTerminal] API Provider Mode enabled for this terminal session.
[WebTerminal] Run AGY in this same terminal, then connect your external harness with:

OPENAI_BASE_URL={baseUrl}
OPENAI_API_KEY={result.PlaintextApiKey}
OPENAI_MODEL=agy

[WebTerminal] This key is shown once. Regenerate it from the admin panel if needed.

""";

        await terminalSession.EmitSystemOutputAsync(message.Replace("\n", "\r\n"), recordInScrollback: false);
    }

    private string GetOrigin()
    {
        var request = Context.GetHttpContext()?.Request;
        if (request == null)
        {
            return "http://gpt.dotge.net:5255";
        }

        return $"{request.Scheme}://{request.Host}";
    }

    /// <summary>
    /// Returns the list of sessions for the current user.
    /// Includes both live sessions and persisted (restorable) sessions.
    /// </summary>
    public List<object> ListSessions()
    {
        if (!TryGetUserId(out var userId)) return new List<object>();

        var result = new List<object>();

        // 1. Live sessions
        var liveSessions = _sessionManager.GetSessionsForUser(userId);
        var liveIds = new HashSet<Guid>(liveSessions.Select(s => s.SessionId));
        foreach (var s in liveSessions)
        {
            result.Add(new { s.SessionId, s.WorkingDirectory, CreatedAt = s.CreatedAt.ToString("O"), IsLive = true });
        }

        // 2. Persisted sessions not already live (restorable)
        var persisted = _persistenceService.GetPersistedSessions()
            .Where(p => p.OwnerUserId == userId && !liveIds.Contains(p.SessionId));
        foreach (var p in persisted)
        {
            result.Add(new { p.SessionId, p.WorkingDirectory, CreatedAt = p.CreatedAt.ToString("O"), IsLive = false });
        }

        return result;
    }

    /// <summary>
    /// Restore a previously persisted session by creating a new process at the saved working directory.
    /// </summary>
    public async Task<HubResponse> RestoreSession(Guid tabId, string workingDirectory)
    {
        if (!TryGetUserId(out var userId)) return HubResponse.Fail(AppFailure.Unauthorized);

        var persistedSession = _persistenceService.GetPersistedSessions().FirstOrDefault(p => p.SessionId == tabId);
        if (persistedSession.SessionId == Guid.Empty || persistedSession.OwnerUserId != userId)
        {
            return HubResponse.Fail(AppFailure.Unauthorized);
        }

        var username = Context.User?.Identity?.Name ?? "unknown";
        var isAdmin = Context.User?.IsInRole("Admin") == true;
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var initScript = System.IO.Path.Combine(baseDir, "init.ps1");
        var homeDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.CurrentDirectory, "home", username));

        if (!System.IO.Directory.Exists(homeDir)) System.IO.Directory.CreateDirectory(homeDir);

        var args = $"-NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -File \"{initScript}\" \"{homeDir}\"";
        if (isAdmin) args += " -IsAdmin";

        var options = new TerminalLaunchOptions(
            Executable: "powershell.exe",
            Arguments: args,
            WorkingDirectory: workingDirectory, // use the saved working directory for restore
            Environment: null,
            Columns: 80,
            Rows: 24
        );

        var result = await _sessionManager.CreateSessionAsync(userId, tabId, options);
        if (!result.IsSuccess) return HubResponse.Fail(result.Failure!);

        var session = result.Value!;

        SetupSessionCallbacks(session, tabId);

        // Load persisted scrollback buffer from previous server run
        var persistedScrollback = _persistenceService.GetPersistedScrollback(tabId);
        if (persistedScrollback != null && persistedScrollback.Length > 0)
        {
            session.LoadScrollback(persistedScrollback);
        }

        session.Attach(Context.ConnectionId);

        // Replay scrollback to the restoring client
        var scrollback = session.GetScrollbackSnapshot();
        if (scrollback.Length > 0)
        {
            try
            {
                await Clients.Caller.SendAsync("TerminalOutput", tabId, scrollback);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send scrollback on restore");
            }
        }

        _logger.LogInformation("Restored session {TabId} at {WorkDir} for user {UserId} (scrollback: {Bytes}b)",
            tabId, workingDirectory, userId, scrollback.Length);

        return HubResponse.Ok();
    }

    /// <summary>
    /// Wire up OnOutput and OnExited callbacks to broadcast to ALL connected clients (multi-device mirroring).
    /// </summary>
    private void SetupSessionCallbacks(TerminalSession session, Guid tabId)
    {
        session.OnOutput = async (byte[] chunk) =>
        {
            var connections = session.ConnectionIds;
            if (connections.Count == 0) return;
            try
            {
                await _hubContext.Clients.Clients(connections).SendAsync("TerminalOutput", tabId, chunk);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast TerminalOutput to {Count} clients", connections.Count);
            }
        };

        session.OnExited = async (int? exitCode) =>
        {
            ApiServerCommandBuffers.TryRemove(tabId, out _);
            var connections = session.ConnectionIds;
            if (connections.Count == 0) return;
            try
            {
                await _hubContext.Clients.Clients(connections).SendAsync("TerminalExited", tabId, exitCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast TerminalExited");
            }
        };
    }
}
