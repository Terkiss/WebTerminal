using System;
using WebPowerShell.Application.Users.Common;

namespace WebPowerShell.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    (string Token, DateTime Expiration) GenerateToken(LoginResponseDto user);
}
