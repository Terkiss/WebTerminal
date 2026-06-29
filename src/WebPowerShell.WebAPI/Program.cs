using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Users.Commands.ChangePassword;
using WebPowerShell.Application.Users.Commands.Login;
using WebPowerShell.Domain.Common;
using WebPowerShell.Infrastructure.Persistence;
using WebPowerShell.Infrastructure.Persistence.Repositories;
using WebPowerShell.Infrastructure.Security;
using WebPowerShell.Infrastructure.PowerShell;
using WebPowerShell.WebAPI.Hubs;
using WebPowerShell.WebAPI.Middleware;

var builder = WebApplication.CreateBuilder(args);

// DB Context & Repository
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("WebPowerShellDb"));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
});

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPowerShellSessionService, PowerShellSessionService>();
builder.Services.AddHostedService<SessionCleanupWorker>();

// Handlers
builder.Services.AddScoped<LoginCommandHandler>();
builder.Services.AddScoped<ChangePasswordCommandHandler>();

// OpenAPI
builder.Services.AddOpenApi();

// SignalR
builder.Services.AddSignalR(options =>
{
    options.AddFilter<HubExceptionFilter>();
});

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".AspNetCore.Cookies";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// Rate Limiting (IP별 1분 내 최대 5회)
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("LoginLimiter", httpContext =>
    {
        string ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        var responseBody = JsonSerializer.Serialize(AppFailure.RateLimitExceeded);
        await context.HttpContext.Response.WriteAsync(responseBody, token);
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<TerminalHub>("/hubs/terminal");

// Password Expiry Middleware
app.UseMiddleware<PasswordExpiryMiddleware>();

// Endpoints
app.MapPost("/api/auth/login", async (LoginCommand command, LoginCommandHandler handler, HttpContext httpContext) =>
{
    var result = await handler.HandleAsync(command);
    if (result.IsFailure)
    {
        return Results.Json(result.Failure, statusCode: StatusCodes.Status401Unauthorized);
    }

    var response = result.Value!;
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, response.UserId.ToString()),
        new Claim(ClaimTypes.Name, response.Username)
    };

    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var authProperties = new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
    };

    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);

    return Results.Ok(response);
})
.RequireRateLimiting("LoginLimiter");

app.MapPost("/api/auth/change-password", async (ChangePasswordCommand command, ChangePasswordCommandHandler handler, HttpContext httpContext) =>
{
    var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
    if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
    {
        return Results.Json(AppFailure.Unauthorized, statusCode: StatusCodes.Status401Unauthorized);
    }

    command.UserId = userId;

    var result = await handler.HandleAsync(command);
    if (result.IsFailure)
    {
        return Results.Json(result.Failure, statusCode: StatusCodes.Status400BadRequest);
    }

    return Results.Ok(new { Success = true });
})
.RequireAuthorization();

app.MapPost("/api/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { Success = true });
})
.RequireAuthorization();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/api/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.RequireAuthorization();


using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    if (!context.Users.Any())
    {
        context.Users.Add(new WebPowerShell.Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            Username = "operator01",
            PasswordHash = hasher.HashPassword("Password123!"),
            LastPasswordChangeDate = DateTimeOffset.UtcNow,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        context.SaveChanges();
    }
}

app.Run();


record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

// For integration tests
public partial class Program { }
