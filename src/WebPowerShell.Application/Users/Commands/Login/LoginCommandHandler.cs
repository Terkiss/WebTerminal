using System;
using System.Threading;
using System.Threading.Tasks;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Users.Common;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Application.Users.Commands.Login
{
    public class LoginCommandHandler
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;

        public LoginCommandHandler(IUserRepository userRepository, IPasswordHasher passwordHasher)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
        }

        public async Task<Result<LoginResponseDto>> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result<LoginResponseDto>.Fail(AppFailure.Unauthorized);
            }

            var userResult = await _userRepository.GetByUsernameAsync(command.Username, cancellationToken);
            if (userResult.IsFailure || userResult.Value == null)
            {
                return Result<LoginResponseDto>.Fail(AppFailure.Unauthorized);
            }

            var user = userResult.Value;
            if (!user.IsActive)
            {
                return Result<LoginResponseDto>.Fail(AppFailure.Unauthorized);
            }

            if (!_passwordHasher.VerifyPassword(command.Password, user.PasswordHash))
            {
                return Result<LoginResponseDto>.Fail(AppFailure.Unauthorized);
            }

            bool isPasswordExpired = DateTimeOffset.UtcNow - user.LastPasswordChangeDate >= TimeSpan.FromDays(7);

            var response = new LoginResponseDto
            {
                UserId = user.Id,
                Username = user.Username,
                IsPasswordExpired = isPasswordExpired
            };

            return Result<LoginResponseDto>.Success(response);
        }
    }
}
