using System;

namespace WebPowerShell.Application.Users.Common
{
    public class LoginResponseDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public bool IsPasswordExpired { get; set; }
        public bool IsAdmin { get; set; }
    }
}
