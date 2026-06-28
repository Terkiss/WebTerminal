using System;

namespace WebPowerShell.Domain.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTimeOffset LastPasswordChangeDate { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int FailedLoginCount { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}
