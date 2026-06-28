using System;
using Microsoft.Extensions.Logging;
using WebPowerShell.Application.Common.Interfaces;

namespace WebPowerShell.Infrastructure.Security
{
    public class BCryptPasswordHasher : IPasswordHasher
    {
        private readonly ILogger<BCryptPasswordHasher> _logger;
        private const int WorkFactor = 11;

        public BCryptPasswordHasher(ILogger<BCryptPasswordHasher> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string HashPassword(string password)
        {
            ArgumentNullException.ThrowIfNull(password);

            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            ArgumentNullException.ThrowIfNull(password);

            if (string.IsNullOrEmpty(hashedPassword))
            {
                return false;
            }

            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Password verification failed due to exception.");
                return false;
            }
        }
    }
}
