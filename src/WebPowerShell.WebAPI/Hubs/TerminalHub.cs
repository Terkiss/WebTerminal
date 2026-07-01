using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using WebPowerShell.Domain.Common;
using WebPowerShell.Infrastructure.ConPTY;

namespace WebPowerShell.WebAPI.Hubs;

[Authorize]
public class TerminalHub : Hub
{
    private readonly ILogger<TerminalHub> _logger;
    private readonly ITerminalSessionManager _sessionManager;
    private readonly IHubContext<TerminalHub> _hubContext;

    public TerminalHub(ILogger<TerminalHub> logger, ITerminalSessionManager sessionManager, IHubContext<TerminalHub> hubContext)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _hubContext = hubContext;
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

        // We DO NOT close sessions immediately. We just Detach them.
        // TerminalSessionManager will clean them up after the Grace Period.
        
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<HubResponse> CreateSession(Guid tabId)
    {
        if (!TryGetUserId(out var userId)) return HubResponse.Fail(AppFailure.Unauthorized);

        // Launch powershell.exe natively. ConPTY will automatically translate output to UTF-8.
        // We avoid 'chcp 65001' because it breaks East Asian Character width calculation in ConPTY.
        var options = new TerminalLaunchOptions(
            Executable: "powershell.exe",
            Arguments: "-NoLogo",
            WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment: null,
            Columns: 80,
            Rows: 24
        );

        var result = await _sessionManager.CreateSessionAsync(userId, tabId, options);
        if (!result.IsSuccess) return HubResponse.Fail(result.Failure!);
        
        var session = result.Value!;
        
        session.OnOutput = async (byte[] chunk) => {
            try { await _hubContext.Clients.Client(session.ConnectionId ?? "").SendAsync("TerminalOutput", tabId, chunk); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send TerminalOutput"); }
        };
        
        session.OnExited = async (int? exitCode) => {
            try { await _hubContext.Clients.Client(session.ConnectionId ?? "").SendAsync("TerminalExited", tabId, exitCode); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send TerminalExited"); }
        };

        // Automatically attach
        session.Attach(Context.ConnectionId);

        return HubResponse.Ok();
    }

    public async Task<HubResponse> AttachSession(Guid tabId)
    {
        if (!TryGetUserId(out var userId)) return HubResponse.Fail(AppFailure.Unauthorized);

        var result = _sessionManager.GetSession(tabId);
        if (!result.IsSuccess) return HubResponse.Fail(result.Failure!);
        
        var session = result.Value!;
        if (session.OwnerUserId != userId) return HubResponse.Fail(AppFailure.Unauthorized);

        session.Attach(Context.ConnectionId);
        return HubResponse.Ok();
    }
    
    public async Task<HubResponse> DetachSession(Guid tabId)
    {
        if (!TryGetUserId(out var userId)) return HubResponse.Fail(AppFailure.Unauthorized);

        var result = _sessionManager.GetSession(tabId);
        if (!result.IsSuccess) return HubResponse.Fail(result.Failure!);
        
        var session = result.Value!;
        if (session.OwnerUserId != userId) return HubResponse.Fail(AppFailure.Unauthorized);

        session.Detach();
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

        await session.SendInputAsync(input);
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

        var closeResult = await _sessionManager.CloseSessionAsync(tabId);
        if (!closeResult.IsSuccess) return HubResponse.Fail(closeResult.Failure!);

        return HubResponse.Ok();
    }

    private bool TryGetUserId(out Guid userId)
    {
        return Guid.TryParse(Context.UserIdentifier, out userId);
    }
}

