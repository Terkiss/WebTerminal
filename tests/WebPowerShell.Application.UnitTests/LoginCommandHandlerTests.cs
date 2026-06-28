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
        private readonly LoginCommandHandler _handler;

        public LoginCommandHandlerTests()
        {
            _userRepository = Substitute.For<IUserRepository>();
            _passwordHasher = Substitute.For<IPasswordHasher>();
            _handler = new LoginCommandHandler(_userRepository, _passwordHasher);
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
                LastPasswordChangeDate = DateTimeOffset.UtcNow.AddDays(-6) // Less than 7 days
            };

            var command = new LoginCommand { Username = username, Password = password };

            _userRepository.GetByUsernameAsync(username, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(password, hashedPassword)
                .Returns(true);

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(user.Id, result.Value.UserId);
            Assert.Equal(user.Username, result.Value.Username);
            Assert.False(result.Value.IsPasswordExpired);
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
                LastPasswordChangeDate = DateTimeOffset.UtcNow.AddDays(-8) // More than 7 days
            };

            var command = new LoginCommand { Username = username, Password = password };

            _userRepository.GetByUsernameAsync(username, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(password, hashedPassword)
                .Returns(true);

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
                LastPasswordChangeDate = DateTimeOffset.UtcNow
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
        public async Task HandleAsync_IncorrectPassword_ReturnsUnauthorized()
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
                LastPasswordChangeDate = DateTimeOffset.UtcNow
            };

            var command = new LoginCommand { Username = username, Password = password };

            _userRepository.GetByUsernameAsync(username, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(password, hashedPassword)
                .Returns(false); // Verification fails

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.Unauthorized, result.Failure);
        }
    }
}
