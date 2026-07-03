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
using WebPowerShell.Infrastructure.ConPTY;
using WebPowerShell.WebAPI.Hubs;
using WebPowerShell.WebAPI.Middleware;
using WebPowerShell.Application.Services;

// Global unhandled exception trap — log before crash
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    Console.Error.WriteLine($"[FATAL] UnhandledException: {e.ExceptionObject}");
};
TaskScheduler.UnobservedTaskException += (sender, e) =>
{
    Console.Error.WriteLine($"[FATAL] UnobservedTaskException: {e.Exception}");
    e.SetObserved();
};

var builder = WebApplication.CreateBuilder(args);


// DB Context (AuditLog only)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("WebPowerShellDb"));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
});

// User Repository: TeruTeruPandas DataFrame (in-memory) + SQLite persistence
builder.Services.AddSingleton<TeruTeruPandasUserRepository>();
builder.Services.AddSingleton<IUserRepository>(sp => sp.GetRequiredService<TeruTeruPandasUserRepository>());
builder.Services.AddSingleton<WebPowerShell.Infrastructure.Persistence.MemoryPersistenceService>();
builder.Services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp =>
    sp.GetRequiredService<WebPowerShell.Infrastructure.Persistence.MemoryPersistenceService>());
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITerminalSessionManager, TerminalSessionManager>();

// Handlers
builder.Services.AddScoped<LoginCommandHandler>();
builder.Services.AddScoped<ChangePasswordCommandHandler>();
builder.Services.AddScoped<WebPowerShell.Application.Users.Commands.CreateUser.CreateUserCommandHandler>();

// File Manager Service
builder.Services.AddSingleton<FileManagerService>();

builder.Services.AddSingleton<WebPowerShell.Infrastructure.Services.SystemMetricsService>();
builder.Services.AddControllers();


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
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
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


app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
        ctx.Context.Response.Headers.Append("Expires", "0");
    }
});

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
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

    if (response.IsAdmin)
    {
        claims.Add(new Claim(ClaimTypes.Role, "Admin"));
    }

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

app.MapGet("/api/users/preferences", async (HttpContext httpContext, IUserRepository userRepo) =>
{
    var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
    if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
    {
        return Results.Json(AppFailure.Unauthorized, statusCode: StatusCodes.Status401Unauthorized);
    }
    var userResult = await userRepo.GetByIdAsync(userId);
    if (userResult.IsFailure) return Results.Json(userResult.Failure, statusCode: StatusCodes.Status401Unauthorized);
    
    var prefs = userResult.Value!.Preferences;
    if (string.IsNullOrEmpty(prefs)) {
        return Results.Ok(new { fontSize = 14, themeBackground = "#090d16", themeForeground = "#cbd5e1" });
    }
    try {
        var parsed = JsonSerializer.Deserialize<object>(prefs);
        return Results.Ok(parsed);
    } catch {
        return Results.Ok(new { fontSize = 14, themeBackground = "#090d16", themeForeground = "#cbd5e1" });
    }
})
.RequireAuthorization();

app.MapPut("/api/users/preferences", async (HttpRequest req, HttpContext httpContext, IUserRepository userRepo) =>
{
    var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
    if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
    {
        return Results.Json(AppFailure.Unauthorized, statusCode: StatusCodes.Status401Unauthorized);
    }
    
    using var reader = new System.IO.StreamReader(req.Body);
    var body = await reader.ReadToEndAsync();
    
    var userResult = await userRepo.GetByIdAsync(userId);
    if (userResult.IsFailure) return Results.Json(userResult.Failure, statusCode: StatusCodes.Status401Unauthorized);
    
    var user = userResult.Value!;
    user.Preferences = body;
    user.UpdatedAt = DateTimeOffset.UtcNow;
    await userRepo.SaveAsync(user);
    
    return Results.Ok(new { success = true });
})
.RequireAuthorization();



// File Manager Endpoints
app.MapGet("/api/files/list", (string? path, FileManagerService fileManagerService) =>
{
    try
    {
        var items = fileManagerService.ListFiles(path ?? "");
        return Results.Ok(new { currentPath = string.IsNullOrEmpty(path) ? "/" : path, items });
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
})
.RequireAuthorization();

app.MapDelete("/api/files/delete", (string path, FileManagerService fileManagerService) =>
{
    try
    {
        fileManagerService.DeleteItem(path);
        return Results.Ok(new { success = true });
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
})
.RequireAuthorization();

app.MapGet("/api/files/download", (string path, FileManagerService fileManagerService) =>
{
    try
    {
        var filePath = fileManagerService.GetFilePath(path);
        var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(filePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }
        return Results.File(System.IO.File.OpenRead(filePath), contentType, Path.GetFileName(filePath));
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
})
.RequireAuthorization();

app.MapPost("/api/files/upload", async (HttpRequest request, string? path, FileManagerService fileManagerService) =>
{
    try
    {
        if (!request.HasFormContentType) return Results.BadRequest(new { message = "Form content type required." });
        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();
        if (file != null && file.Length > 0)
        {
            using (var stream = file.OpenReadStream())
            {
                fileManagerService.SaveFile(path ?? "", file.FileName, stream);
            }
            return Results.Ok(new { success = true });
        }
        return Results.BadRequest(new { message = "Empty file." });
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
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


// Seed terukiss admin into TeruTeruPandas DataFrame
{
    var userRepo = app.Services.GetRequiredService<IUserRepository>();
    var hasher = app.Services.GetRequiredService<IPasswordHasher>();
    var existing = await userRepo.GetByUsernameAsync("terukiss");
    if (!existing.IsSuccess)
    {
        await userRepo.SaveAsync(new WebPowerShell.Domain.Entities.User
        {
            Id = Guid.Parse("a0a0a0a0-b1b1-c2c2-d3d3-e4e4e4e4e4e4"),
            Username = "terukiss",
            PasswordHash = hasher.HashPassword("dbslwms@skshgk1"),
            LastPasswordChangeDate = DateTimeOffset.UtcNow,
            IsActive = true,
            IsAdmin = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}

app.Run();


record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

// For integration tests
public partial class Program { }
