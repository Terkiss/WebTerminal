using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace WebPowerShell.WebAPI.Hubs;

[Authorize]
public class TerminalHub : Hub
{
    private readonly ILogger<TerminalHub> _logger;

    public TerminalHub(ILogger<TerminalHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("User {UserId} connected with connection ID {ConnectionId}", userId, connectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
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
}
