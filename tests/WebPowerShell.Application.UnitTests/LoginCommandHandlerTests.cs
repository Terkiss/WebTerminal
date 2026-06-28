using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Users.Commands.Login;
using WebPowerShell.Application.Users.Common;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;
using Xunit;

namespace WebPowerShell.Application.UnitTests
{
    public class LoginCommandHandlerTests
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly FakeTimeProvider _timeProvider;
        private readonly LoginCommandHandler _handler;

        public LoginCommandHandlerTests()
        {
            _userRepository = Substitute.For<IUserRepository>();
            _passwordHasher = Substitute.For<IPasswordHasher>();
            _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 29, 3, 0, 0, TimeSpan.Zero));
            _handler = new LoginCommandHandler(_userRepository, _passwordHasher, _timeProvider);
        }

        [Fact]
        public async Task HandleAsync_ValidCredentialsAndPasswordNotExpired_ReturnsSuccessWithNotExpiredDto()
        {
            // Arrange
            var username = "testuser";
            var password = "correctpassword";
            var hashedPassword = "hashedpassword";

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = hashedPassword,
                IsActive = true,
                LastPasswordChangeDate = _timeProvider.GetUtcNow().AddDays(-6) // Less than 7 days
            };

            var command = new LoginCommand { Username = username, Password = password };

            _userRepository.GetByUsernameAsync(username, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(password, hashedPassword)
                .Returns(true);

            _userRepository.SaveAsync(user, Arg.Any<CancellationToken>())
                .Returns(Result<bool>.Success(true));

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(user.Id, result.Value.UserId);
            Assert.Equal(user.Username, result.Value.Username);
            Assert.False(result.Value.IsPasswordExpired);
            Assert.Equal(0, user.FailedLoginCount);
            Assert.Null(user.LockedUntil);
        }

        [Fact]
        public async Task HandleAsync_ValidCredentialsAndPasswordExpired_ReturnsSuccessWithExpiredDto()
        {
            // Arrange
            var username = "testuser";
            var password = "correctpassword";
            var hashedPassword = "hashedpassword";

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = hashedPassword,
                IsActive = true,
                LastPasswordChangeDate = _timeProvider.GetUtcNow().AddDays(-8) // More than 7 days
            };

            var command = new LoginCommand { Username = username, Password = password };

            _userRepository.GetByUsernameAsync(username, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(password, hashedPassword)
                .Returns(true);

            _userRepository.SaveAsync(user, Arg.Any<CancellationToken>())
                .Returns(Result<bool>.Success(true));

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(user.Id, result.Value.UserId);
            Assert.Equal(user.Username, result.Value.Username);
            Assert.True(result.Value.IsPasswordExpired);
        }

        [Fact]
        public async Task HandleAsync_UserDoesNotExist_ReturnsUnauthorized()
        {
            // Arrange
            var username = "nonexistent";
            var password = "anypassword";
            var command = new LoginCommand { Username = username, Password = password };

            _userRepository.GetByUsernameAsync(username, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Fail(AppFailure.Unauthorized));

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.Unauthorized, result.Failure);
        }

        [Fact]
        public async Task HandleAsync_UserIsInactive_ReturnsUnauthorized()
        {
            // Arrange
            var username = "inactiveuser";
            var password = "correctpassword";
            var hashedPassword = "hashedpassword";

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = hashedPassword,
                IsActive = false, // Inactive
                LastPasswordChangeDate = _timeProvider.GetUtcNow()
            };

            var command = new LoginCommand { Username = username, Password = password };

            _userRepository.GetByUsernameAsync(username, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.Unauthorized, result.Failure);
        }

        [Fact]
        public async Task HandleAsync_IncorrectPassword_IncrementsFailedCountAndSaves()
        {
            // Arrange
            var username = "testuser";
            var password = "wrongpassword";
            var hashedPassword = "hashedpassword";

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = hashedPassword,
                IsActive = true,
                FailedLoginCount = 2,
                LastPasswordChangeDate = _timeProvider.GetUtcNow()
            };

            var command = new LoginCommand { Username = username, Password = password };

            _userRepository.GetByUsernameAsync(username, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(password, hashedPassword)
                .Returns(false);

            _userRepository.SaveAsync(user, Arg.Any<CancellationToken>())
                .Returns(Result<bool>.Success(true));

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.Unauthorized, result.Failure);
            Assert.Equal(3, user.FailedLoginCount);
            Assert.Null(user.LockedUntil);
            await _userRepository.Received(1).SaveAsync(user, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task HandleAsync_IncorrectPasswordFiveTimes_LocksAccount()
        {
            // Arrange
            var username = "testuser";
            var password = "wrongpassword";
            var hashedPassword = "hashedpassword";

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = hashedPassword,
                IsActive = true,
                FailedLoginCount = 4,
                LastPasswordChangeDate = _timeProvider.GetUtcNow()
            };

            var command = new LoginCommand { Username = username, Password = password };

            _userRepository.GetByUsernameAsync(username, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(password, hashedPassword)
                .Returns(false);

            _userRepository.SaveAsync(user, Arg.Any<CancellationToken>())
                .Returns(Result<bool>.Success(true));

            var expectedLockUntil = _timeProvider.GetUtcNow().AddMinutes(1);

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.Unauthorized, result.Failure);
            Assert.Equal(5, user.FailedLoginCount);
            Assert.NotNull(user.LockedUntil);
            Assert.Equal(expectedLockUntil, user.LockedUntil.Value);
            await _userRepository.Received(1).SaveAsync(user, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task HandleAsync_AccountIsLocked_ReturnsUnauthorizedImmediately()
        {
            // Arrange
            var username = "testuser";
            var password = "correctpassword";
            var hashedPassword = "hashedpassword";

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = hashedPassword,
                IsActive = true,
                FailedLoginCount = 5,
                LockedUntil = _timeProvider.GetUtcNow().AddSeconds(30),
                LastPasswordChangeDate = _timeProvider.GetUtcNow().AddDays(-1)
            };

            var command = new LoginCommand { Username = username, Password = password };

            _userRepository.GetByUsernameAsync(username, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.Unauthorized, result.Failure);

            // Should not check password nor reset lock status
            _passwordHasher.DidNotReceiveWithAnyArgs().VerifyPassword(default!, default!);
            await _userRepository.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
        }
    }
}
