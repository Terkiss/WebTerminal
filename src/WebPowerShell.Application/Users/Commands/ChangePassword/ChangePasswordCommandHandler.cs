using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Application.Users.Commands.ChangePassword
{
    public class ChangePasswordCommandHandler
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly TimeProvider _timeProvider;

        public ChangePasswordCommandHandler(IUserRepository userRepository, IPasswordHasher passwordHasher, TimeProvider? timeProvider = null)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<Result<bool>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                return Result<bool>.Fail(AppFailure.Unauthorized);
            }

            var userResult = await _userRepository.GetByIdAsync(command.UserId, cancellationToken);
            if (userResult.IsFailure)
            {
                return Result<bool>.Fail(userResult.Failure ?? AppFailure.InternalError);
            }
            if (userResult.Value == null)
            {
                return Result<bool>.Fail(AppFailure.Unauthorized);
            }

            var user = userResult.Value;

            if (!user.IsActive)
            {
                return Result<bool>.Fail(AppFailure.Unauthorized);
            }

            if (!_passwordHasher.VerifyPassword(command.CurrentPassword, user.PasswordHash))
            {
                return Result<bool>.Fail(AppFailure.Unauthorized);
            }

            if (command.CurrentPassword == command.NewPassword || _passwordHasher.VerifyPassword(command.NewPassword, user.PasswordHash))
            {
                return Result<bool>.Fail(new AppFailure("InvalidPassword", "새 비밀번호는 기존 비밀번호와 달라야 합니다."));
            }

            if (string.IsNullOrEmpty(command.NewPassword) || command.NewPassword.Length < 8)
            {
                return Result<bool>.Fail(new AppFailure("InvalidPassword", "비밀번호는 최소 8자 이상이어야 하며, 영문 대/소문자, 숫자, 특수문자 중 3가지 이상을 조합해야 합니다."));
            }

            bool hasUpper = false;
            bool hasLower = false;
            bool hasDigit = false;
            bool hasSpecial = false;

            const string SpecialCharacters = "!@#$%^&*()_+-=[]{}|;':\",./<>?`~\\";
            foreach (char c in command.NewPassword)
            {
                if (c >= 'A' && c <= 'Z') hasUpper = true;
                else if (c >= 'a' && c <= 'z') hasLower = true;
                else if (c >= '0' && c <= '9') hasDigit = true;
                else if (SpecialCharacters.Contains(c)) hasSpecial = true;
            }

            int typesCount = 0;
            if (hasUpper) typesCount++;
            if (hasLower) typesCount++;
            if (hasDigit) typesCount++;
            if (hasSpecial) typesCount++;

            if (typesCount < 3)
            {
                return Result<bool>.Fail(new AppFailure("InvalidPassword", "비밀번호는 최소 8자 이상이어야 하며, 영문 대/소문자, 숫자, 특수문자 중 3가지 이상을 조합해야 합니다."));
            }

            user.PasswordHash = _passwordHasher.HashPassword(command.NewPassword);
            user.LastPasswordChangeDate = _timeProvider.GetUtcNow();
            user.FailedLoginCount = 0;
            user.LockedUntil = null;

            var saveResult = await _userRepository.SaveAsync(user, cancellationToken);
            if (saveResult.IsFailure)
            {
                return Result<bool>.Fail(saveResult.Failure ?? AppFailure.InternalError);
            }

            return Result<bool>.Success(true);
        }
    }
}
