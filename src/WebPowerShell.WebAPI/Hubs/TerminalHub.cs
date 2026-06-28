using System;
using System.Security.Claims;
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

        var result = await _sessionService.ExecuteCommandAsync(
            userId,
            tabId,
            command,
            async (streamData, ct) =>
            {
                await Clients.Caller.SendAsync("ReceiveOutput", tabId, streamData, ct);
            },
            Context.ConnectionAborted);

        if (!result.IsSuccess)
        {
            return HubResponse.Fail(result.Failure!);
        }

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
