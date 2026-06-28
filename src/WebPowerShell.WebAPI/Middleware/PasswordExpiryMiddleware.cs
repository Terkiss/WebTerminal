using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Common;

namespace WebPowerShell.WebAPI.Middleware
{
    public class PasswordExpiryMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly TimeProvider _timeProvider;

        public PasswordExpiryMiddleware(RequestDelegate next, TimeProvider timeProvider)
        {
            _next = next;
            _timeProvider = timeProvider;
        }

        public async Task InvokeAsync(HttpContext context, IUserRepository userRepository)
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            if (path.Equals("/api/auth/change-password", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                var userResult = await userRepository.GetByIdAsync(userId);
                if (userResult.IsSuccess && userResult.Value != null)
                {
                    var user = userResult.Value;
                    if (_timeProvider.GetUtcNow() - user.LastPasswordChangeDate >= TimeSpan.FromDays(7))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        
                        var responseBody = JsonSerializer.Serialize(AppFailure.PasswordExpired);
                        await context.Response.WriteAsync(responseBody);
                        return;
                    }
                }
            }

            await _next(context);
        }
    }
}
