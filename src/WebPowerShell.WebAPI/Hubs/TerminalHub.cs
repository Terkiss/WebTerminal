using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Common;

namespace WebPowerShell.WebAPI.Hubs;

[Authorize]
public class TerminalHub : Hub
{
    private readonly ILogger<TerminalHub> _logger;
    private readonly IPowerShellSessionService _sessionService;
    private readonly IHubContext<TerminalHub> _hubContext;

    public TerminalHub(ILogger<TerminalHub> logger, IPowerShellSessionService sessionService, IHubContext<TerminalHub> hubContext)
    {
        _logger = logger;
        _sessionService = sessionService;
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
            var result = await _sessionService.CloseAllSessionsForUserAsync(userId, CancellationToken.None);
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

        var result = await _sessionService.CreateSessionAsync(
            userId, 
            tabId,
            onOutput: async (output) => 
            {
                try { await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveOutput", tabId, output); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to send ReceiveOutput"); }
            },
            onError: async (error) => 
            {
                try { await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveError", tabId, error); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to send ReceiveError"); }
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

        // PtyProcess에 직접 명령 문자열(또는 개별 키 입력)을 전달
        // 기존 프론트엔드가 완성된 커맨드를 보내는 방식이라면 \n을 추가해서 보냄
        string inputToWrite = command;

        var result = await _sessionService.WriteInputAsync(userId, tabId, inputToWrite, Context.ConnectionAborted);

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

        var result = await _sessionService.StopCommandAsync(userId, tabId, Context.ConnectionAborted);
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

        var result = await _sessionService.CloseSessionAsync(userId, tabId, Context.ConnectionAborted);
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
