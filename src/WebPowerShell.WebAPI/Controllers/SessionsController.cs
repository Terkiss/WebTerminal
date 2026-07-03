using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebPowerShell.Infrastructure.ConPTY;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace WebPowerShell.WebAPI.Controllers;

public class InputRequest
{
    public string Input { get; set; } = "";
}

public class CallbackRequest
{
    public string Result { get; set; } = "";
}

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + JwtBearerDefaults.AuthenticationScheme)]
public class SessionsController : ControllerBase
{
    private readonly ITerminalSessionManager _sessionManager;

    public SessionsController(ITerminalSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [HttpGet("me")]
    public IActionResult GetMySessions()
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized();
        }

        var sessions = _sessionManager.GetSessionsForUser(userId);
        return Ok(sessions.Select(s => new { s.SessionId, s.CreatedAt, s.LastActivityAt, s.HasConnections }));
    }

    [HttpGet("{id}/output")]
    public IActionResult GetOutput(Guid id)
    {
        var sessionResult = _sessionManager.GetSession(id);
        if (sessionResult.IsFailure) return NotFound(sessionResult.Failure);
        
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized();
        }
        
        if (sessionResult.Value!.OwnerUserId != userId)
        {
            return Forbid();
        }

        var scrollback = sessionResult.Value!.GetScrollbackSnapshot();
        var outputText = Encoding.UTF8.GetString(scrollback);
        
        return Ok(new { output = outputText });
    }

    [HttpPost("{id}/input")]
    public async Task<IActionResult> InjectInput(Guid id, [FromBody] InputRequest request)
    {
        var sessionResult = _sessionManager.GetSession(id);
        if (sessionResult.IsFailure) return NotFound(sessionResult.Failure);

        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized();
        }
        
        if (sessionResult.Value!.OwnerUserId != userId)
        {
            return Forbid();
        }

        var bytes = Encoding.UTF8.GetBytes(request.Input ?? "");
        await sessionResult.Value!.SendInputAsync(bytes);
        
        return Ok(new { success = true });
    }

    [HttpPost("{id}/callback")]
    public IActionResult PostCallback(Guid id, [FromBody] CallbackRequest request)
    {
        var sessionResult = _sessionManager.GetSession(id);
        if (sessionResult.IsFailure) return NotFound(sessionResult.Failure);

        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized();
        }

        if (sessionResult.Value!.OwnerUserId != userId)
        {
            return Forbid();
        }

        _sessionManager.StoreCallbackResult(id, request.Result ?? "");
        return Ok(new { success = true });
    }

    [HttpGet("{id}/result")]
    public IActionResult GetResult(Guid id)
    {
        var sessionResult = _sessionManager.GetSession(id);
        if (sessionResult.IsFailure) return NotFound(sessionResult.Failure);

        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized();
        }
        
        if (sessionResult.Value!.OwnerUserId != userId)
        {
            return Forbid();
        }

        var result = _sessionManager.RetrieveCallbackResult(id);
        if (result == null)
        {
            return Ok(new { result = (string?)null });
        }
        
        return Ok(new { result = result });
    }
}
