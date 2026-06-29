using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Common.Models;
using WebPowerShell.Domain.Common;

namespace WebPowerShell.WebAPI.Hubs;

[Authorize]
public class TerminalHub : Hub
{
    private readonly ILogger<TerminalHub> _logger;
    private readonly ITeruTeruEngine _engine;
    private readonly IHubContext<TerminalHub> _hubContext;

    public TerminalHub(ILogger<TerminalHub> logger, ITeruTeruEngine engine, IHubContext<TerminalHub> hubContext)
    {
        _logger = logger;
        _engine = engine;
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

        if (exception != null)
        {
            _logger.LogWarning(exception, "User {UserId} disconnected with connection ID {ConnectionId} due to an error", userIdString, connectionId);
        }
        else
        {
            _logger.LogInformation("User {UserId} disconnected with connection ID {ConnectionId}", userIdString, connectionId);
        }

        if (TryGetUserId(out var userId))
        {
            // Do not use Context.ConnectionAborted since the connection is already disconnected/aborted
            var result = await _engine.CloseAllSessionsForUserAsync(userId, CancellationToken.None);
            if (result.IsSuccess)
            {
                _logger.LogInformation("Closed {Count} sessions for user {UserId} upon disconnect.", result.Value, userId);
            }
            else
            {
                _logger.LogWarning("Failed to close sessions for user {UserId} upon disconnect: {FailureMessage}", userId, result.Failure?.Message);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<HubResponse> OpenTab(Guid tabId)
    {
        if (!TryGetUserId(out var userId))
        {
            return HubResponse.Fail(AppFailure.Unauthorized);
        }

        var connectionId = Context.ConnectionId;

        var result = await _engine.CreateSessionAsync(
            userId, 
            tabId,
            onOutput: async (payload) => 
            {
                try { await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveOutput", tabId, payload); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to send ReceiveOutput"); }
            },
            onExited: async () => 
            {
                try { await _hubContext.Clients.Client(connectionId).SendAsync("SessionExited", tabId); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to send SessionExited"); }
            },
            Context.ConnectionAborted);

        if (!result.IsSuccess)
        {
            return HubResponse.Fail(result.Failure!);
        }

        return HubResponse.Ok();
    }

    public async Task<HubResponse> SendCommand(Guid tabId, string command)
    {
        if (!TryGetUserId(out var userId))
        {
            return HubResponse.Fail(AppFailure.Unauthorized);
        }

        // PTY 방식처럼 실시간 키 입력이 아니라,
        // TeruTeruShell은 완성된 명령어를 전달받아 실행합니다.
        string inputToWrite = command;

        var result = await _engine.ExecuteCommandAsync(userId, tabId, inputToWrite, Context.ConnectionAborted);

        if (!result.IsSuccess)
        {
            return HubResponse.Fail(result.Failure!);
        }

        // 기존 클라이언트가 CommandCompleted를 기다렸으나, PTY 방식에서는 명령 완료 개념이 없으므로
        // 더 이상 CommandCompleted 이벤트를 강제로 발생시키지 않습니다.

        return HubResponse.Ok();
    }

    public async Task<HubResponse> StopCommand(Guid tabId)
    {
        if (!TryGetUserId(out var userId))
        {
            return HubResponse.Fail(AppFailure.Unauthorized);
        }

        var result = await _engine.StopCommandAsync(userId, tabId, Context.ConnectionAborted);
        if (!result.IsSuccess)
        {
            return HubResponse.Fail(result.Failure!);
        }

        return HubResponse.Ok();
    }

    public async Task<HubResponse> CloseTab(Guid tabId)
    {
        if (!TryGetUserId(out var userId))
        {
            return HubResponse.Fail(AppFailure.Unauthorized);
        }

        var result = await _engine.CloseSessionAsync(userId, tabId, Context.ConnectionAborted);
        if (!result.IsSuccess)
        {
            return HubResponse.Fail(result.Failure!);
        }

        return HubResponse.Ok();
    }

    private bool TryGetUserId(out Guid userId)
    {
        return Guid.TryParse(Context.UserIdentifier, out userId);
    }
}
