using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Users.Commands.CreateUser;
using WebPowerShell.Infrastructure.Services;
using WebPowerShell.Infrastructure.ConPTY;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace WebPowerShell.WebAPI.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly SystemMetricsService _metricsService;
    private readonly ITerminalSessionManager _sessionManager;
    private readonly IUserRepository _userRepository;
    private readonly CreateUserCommandHandler _createUserHandler;

    public AdminController(
        SystemMetricsService metricsService,
        ITerminalSessionManager sessionManager,
        IUserRepository userRepository,
        CreateUserCommandHandler createUserHandler)
    {
        _metricsService = metricsService;
        _sessionManager = sessionManager;
        _userRepository = userRepository;
        _createUserHandler = createUserHandler;
    }

    [HttpGet("system")]
    public IActionResult GetSystemMetrics()
    {
        return Ok(_metricsService.GetMetrics());
    }

    [HttpGet("sessions")]
    public IActionResult GetSessions()
    {
        var sessions = _sessionManager.GetAllSessions();
        var result = sessions.Select(s => new
        {
            s.SessionId,
            s.OwnerUserId,
            s.CreatedAt,
            s.LastActivityAt,
            s.WorkingDirectory,
            s.HasConnections,
            ConnectionCount = s.ConnectionIds.Count
        });
        return Ok(result);
    }

    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> CloseSession(Guid sessionId)
    {
        var result = await _sessionManager.CloseSessionAsync(sessionId);
        if (result.IsFailure)
        {
            return BadRequest(result.Failure);
        }
        return Ok(new { Success = true });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var result = await _userRepository.GetAllAsync();
        if (result.IsFailure)
        {
            return BadRequest(result.Failure);
        }

        var users = result.Value!.Select(u => new
        {
            u.Id,
            u.Username,
            u.IsActive,
            u.IsAdmin,
            u.CreatedAt,
            u.UpdatedAt,
            u.FailedLoginCount,
            u.LockedUntil
        });
        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserCommand command)
    {
        var result = await _createUserHandler.HandleAsync(command);
        if (result.IsFailure)
        {
            return BadRequest(result.Failure);
        }
        return Ok(new { UserId = result.Value });
    }

    [HttpDelete("users/{userId}")]
    public async Task<IActionResult> DeleteUser(Guid userId)
    {
        var result = await _userRepository.DeleteAsync(userId);
        if (result.IsFailure)
        {
            return BadRequest(result.Failure);
        }
        return Ok(new { Success = true });
    }
}
