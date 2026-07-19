using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.WebAPI.Services;

public sealed class AdminBootstrapService : IHostedService
{
    private static readonly Guid DefaultAdminId = Guid.Parse("a0a0a0a0-b1b1-c2c2-d3d3-e4e4e4e4e4e4");

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminBootstrapService> _logger;

    public AdminBootstrapService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ILogger<AdminBootstrapService> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var username = _configuration["AdminBootstrap:Username"] ?? "terukiss";
        var password = _configuration["AdminBootstrap:Password"] ?? "dbslwms@skshgk1";
        var resetExisting = bool.TryParse(_configuration["AdminBootstrap:ResetExisting"], out var shouldReset) && shouldReset;

        var existing = await _userRepository.GetByUsernameAsync(username, cancellationToken);
        if (!existing.IsSuccess)
        {
            await _userRepository.SaveAsync(new User
            {
                Id = DefaultAdminId,
                Username = username,
                PasswordHash = _passwordHasher.HashPassword(password),
                LastPasswordChangeDate = DateTimeOffset.UtcNow,
                IsActive = true,
                IsAdmin = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            _logger.LogInformation("Bootstrapped admin account {Username}.", username);
            return;
        }

        if (!resetExisting || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var admin = existing.Value!;
        admin.PasswordHash = _passwordHasher.HashPassword(password);
        admin.LastPasswordChangeDate = DateTimeOffset.UtcNow;
        admin.IsActive = true;
        admin.IsAdmin = true;
        admin.FailedLoginCount = 0;
        admin.LockedUntil = null;
        admin.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.SaveAsync(admin, cancellationToken);

        _logger.LogInformation("Reset configured admin account {Username}.", username);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
