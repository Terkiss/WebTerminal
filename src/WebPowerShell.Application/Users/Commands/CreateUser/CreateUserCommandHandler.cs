using System;
using System.Threading;
using System.Threading.Tasks;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Application.Users.Commands.CreateUser
{
    public class CreateUserCommandHandler
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly TimeProvider _timeProvider;

        public CreateUserCommandHandler(IUserRepository userRepository, IPasswordHasher passwordHasher, TimeProvider? timeProvider = null)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<Result<Guid>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrWhiteSpace(command.Password))
            {
                return Result<Guid>.Fail(AppFailure.InvalidInput);
            }

            var existingUser = await _userRepository.GetByUsernameAsync(command.Username, cancellationToken);
            if (existingUser.IsSuccess)
            {
                return Result<Guid>.Fail(AppFailure.UserAlreadyExists);
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = command.Username,
                PasswordHash = _passwordHasher.HashPassword(command.Password),
                LastPasswordChangeDate = _timeProvider.GetUtcNow(),
                IsActive = true,
                IsAdmin = command.IsAdmin,
                CreatedAt = _timeProvider.GetUtcNow(),
                UpdatedAt = _timeProvider.GetUtcNow()
            };

            await _userRepository.SaveAsync(user, cancellationToken);

            return Result<Guid>.Success(user.Id);
        }
    }
}
