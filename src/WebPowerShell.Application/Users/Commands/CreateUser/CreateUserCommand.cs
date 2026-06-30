using System;

namespace WebPowerShell.Application.Users.Commands.CreateUser
{
    public class CreateUserCommand
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
    }
}
