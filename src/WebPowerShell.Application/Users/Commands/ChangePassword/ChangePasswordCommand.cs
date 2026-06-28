using System;

namespace WebPowerShell.Application.Users.Commands.ChangePassword
{
    public class ChangePasswordCommand
    {
        public Guid UserId { get; set; }
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
