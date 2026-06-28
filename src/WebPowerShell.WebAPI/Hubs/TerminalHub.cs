using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Sessions.Common;
using WebPowerShell.Domain.Common;

namespace WebPowerShell.WebAPI.Hubs;

[Authorize]
public class TerminalHub : Hub
{
    private readonly ILogger<TerminalHub> _logger;
    private readonly IPowerShellSessionService _sessionService;

    public TerminalHub(ILogger<TerminalHub> logger, IPowerShellSessionService sessionService)
    {
        _logger = logger;
        _sessionService = sessionService;
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
        var userId = Context.UserIdentifier ?? "unknown";
        var connectionId = Context.ConnectionId;

        if (exception != null)
        {
            _logger.LogWarning(exception, "User {UserId} disconnected with connection ID {ConnectionId} due to an error", userId, connectionId);
        }
        else
        {
            _logger.LogInformation("User {UserId} disconnected with connection ID {ConnectionId}", userId, connectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<HubResponse> OpenTab(Guid tabId)
    {
        if (!TryGetUserId(out var userId))
        {
            return HubResponse.Fail(AppFailure.Unauthorized);
        }

        var result = await _sessionService.CreateSessionAsync(userId, tabId, Context.ConnectionAborted);
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

        // 명령 시작 이벤트 발송
        await Clients.Caller.SendAsync("CommandStarted", tabId, Context.ConnectionAborted);

        Result<bool>? result = null;
        try
        {
            result = await _sessionService.ExecuteCommandAsync(
                userId,
                tabId,
                command,
                async (streamData, ct) =>
                {
                    // 스트림 데이터 구분 라우팅
                    switch (streamData.Type)
                    {
                        case PowerShellStreamType.Output:
                            await Clients.Caller.SendAsync("ReceiveOutput", tabId, streamData.Content, ct);
                            break;
                        case PowerShellStreamType.Error:
                            await Clients.Caller.SendAsync("ReceiveError", tabId, streamData.Content, ct);
                            break;
                        case PowerShellStreamType.Warning:
                            await Clients.Caller.SendAsync("ReceiveWarning", tabId, streamData.Content, ct);
                            break;
                        case PowerShellStreamType.Verbose:
                            await Clients.Caller.SendAsync("ReceiveVerbose", tabId, streamData.Content, ct);
                            break;
                        case PowerShellStreamType.Debug:
                            await Clients.Caller.SendAsync("ReceiveDebug", tabId, streamData.Content, ct);
                            break;
                        case PowerShellStreamType.Information:
                            await Clients.Caller.SendAsync("ReceiveInformation", tabId, streamData.Content, ct);
                            break;
                    }
                },
                Context.ConnectionAborted);

            if (!result.IsSuccess)
            {
                return HubResponse.Fail(result.Failure!);
            }

            return HubResponse.Ok();
        }
        finally
        {
            bool isSuccess = result is { IsSuccess: true, Value: true };
            await Clients.Caller.SendAsync("CommandCompleted", tabId, isSuccess, Context.ConnectionAborted);
        }
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
