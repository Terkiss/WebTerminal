using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Users.Commands.ChangePassword;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;
using Xunit;

namespace WebPowerShell.Application.UnitTests
{
    public class ChangePasswordCommandHandlerTests
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly FakeTimeProvider _timeProvider;
        private readonly ChangePasswordCommandHandler _handler;

        public ChangePasswordCommandHandlerTests()
        {
            _userRepository = Substitute.For<IUserRepository>();
            _passwordHasher = Substitute.For<IPasswordHasher>();
            _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 29, 3, 0, 0, TimeSpan.Zero));
            _handler = new ChangePasswordCommandHandler(_userRepository, _passwordHasher, _timeProvider);
        }

        [Fact]
        public async Task HandleAsync_ValidRequest_ChangesPasswordSuccessfully()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var currentPassword = "CurrentPassword123!";
            var currentPasswordHash = "current_hashed";
            var newPassword = "NewPassword123!";
            var newPasswordHash = "new_hashed";

            var user = new User
            {
                Id = userId,
                Username = "testuser",
                PasswordHash = currentPasswordHash,
                IsActive = true,
                LastPasswordChangeDate = _timeProvider.GetUtcNow().AddDays(-10),
                FailedLoginCount = 3,
                LockedUntil = _timeProvider.GetUtcNow().AddMinutes(15)
            };

            var command = new ChangePasswordCommand
            {
                UserId = userId,
                CurrentPassword = currentPassword,
                NewPassword = newPassword
            };

            _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(currentPassword, currentPasswordHash)
                .Returns(true);

            _passwordHasher.VerifyPassword(newPassword, currentPasswordHash)
                .Returns(false);

            _passwordHasher.HashPassword(newPassword)
                .Returns(newPasswordHash);

            _userRepository.SaveAsync(user, Arg.Any<CancellationToken>())
                .Returns(Result<bool>.Success(true));

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.True(result.Value);
            Assert.Equal(newPasswordHash, user.PasswordHash);
            Assert.Equal(_timeProvider.GetUtcNow(), user.LastPasswordChangeDate);
            Assert.Equal(0, user.FailedLoginCount);
            Assert.Null(user.LockedUntil);

            await _userRepository.Received(1).SaveAsync(user, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task HandleAsync_GetByIdAsyncFails_ReturnsRepositoryError()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var command = new ChangePasswordCommand
            {
                UserId = userId,
                CurrentPassword = "Current123!",
                NewPassword = "NewPassword123!"
            };

            var expectedFailure = new AppFailure("DatabaseError", "데이터베이스 연결 오류");
            _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Fail(expectedFailure));

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(expectedFailure, result.Failure);
        }

        [Fact]
        public async Task HandleAsync_UserNotFound_ReturnsUnauthorized()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var command = new ChangePasswordCommand
            {
                UserId = userId,
                CurrentPassword = "Current123!",
                NewPassword = "NewPassword123!"
            };

            _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(null!));

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
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "inactiveuser",
                PasswordHash = "hash",
                IsActive = false
            };

            var command = new ChangePasswordCommand
            {
                UserId = userId,
                CurrentPassword = "Current123!",
                NewPassword = "NewPassword123!"
            };

            _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.Unauthorized, result.Failure);
        }

        [Fact]
        public async Task HandleAsync_IncorrectCurrentPassword_ReturnsUnauthorized()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var currentPassword = "WrongCurrent123!";
            var currentPasswordHash = "current_hashed";

            var user = new User
            {
                Id = userId,
                Username = "testuser",
                PasswordHash = currentPasswordHash,
                IsActive = true
            };

            var command = new ChangePasswordCommand
            {
                UserId = userId,
                CurrentPassword = currentPassword,
                NewPassword = "NewPassword123!"
            };

            _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(currentPassword, currentPasswordHash)
                .Returns(false);

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.Unauthorized, result.Failure);
        }

        [Fact]
        public async Task HandleAsync_NewPasswordSameAsCurrentPasswordPlaintext_ReturnsInvalidPassword()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var samePassword = "SamePassword123!";
            var currentPasswordHash = "current_hashed";

            var user = new User
            {
                Id = userId,
                Username = "testuser",
                PasswordHash = currentPasswordHash,
                IsActive = true
            };

            var command = new ChangePasswordCommand
            {
                UserId = userId,
                CurrentPassword = samePassword,
                NewPassword = samePassword
            };

            _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(samePassword, currentPasswordHash)
                .Returns(true);

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.NotNull(result.Failure);
            Assert.Equal("InvalidPassword", result.Failure.ErrorCode);
            Assert.Equal("새 비밀번호는 기존 비밀번호와 달라야 합니다.", result.Failure.Message);
        }

        [Fact]
        public async Task HandleAsync_NewPasswordSameAsCurrentPasswordHashed_ReturnsInvalidPassword()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var currentPassword = "CurrentPassword123!";
            var newPassword = "NewPassword123!";
            var currentPasswordHash = "current_hashed";

            var user = new User
            {
                Id = userId,
                Username = "testuser",
                PasswordHash = currentPasswordHash,
                IsActive = true
            };

            var command = new ChangePasswordCommand
            {
                UserId = userId,
                CurrentPassword = currentPassword,
                NewPassword = newPassword
            };

            _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(currentPassword, currentPasswordHash)
                .Returns(true);

            _passwordHasher.VerifyPassword(newPassword, currentPasswordHash)
                .Returns(true);

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.NotNull(result.Failure);
            Assert.Equal("InvalidPassword", result.Failure.ErrorCode);
            Assert.Equal("새 비밀번호는 기존 비밀번호와 달라야 합니다.", result.Failure.Message);
        }

        [Theory]
        [InlineData("Short1!")]
        [InlineData("abcdefgh")]
        [InlineData("ABCDEFGH")]
        [InlineData("12345678")]
        [InlineData("!!!!!!!!")]
        [InlineData("Abcdefgh")]
        [InlineData("abcde123")]
        [InlineData("abcde!!!")]
        [InlineData("비밀번호123a")]
        public async Task HandleAsync_NewPasswordDoesNotMeetComplexity_ReturnsInvalidPassword(string invalidNewPassword)
        {
            // Arrange
            var userId = Guid.NewGuid();
            var currentPassword = "CurrentPassword123!";
            var currentPasswordHash = "current_hashed";

            var user = new User
            {
                Id = userId,
                Username = "testuser",
                PasswordHash = currentPasswordHash,
                IsActive = true
            };

            var command = new ChangePasswordCommand
            {
                UserId = userId,
                CurrentPassword = currentPassword,
                NewPassword = invalidNewPassword
            };

            _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(Result<User>.Success(user));

            _passwordHasher.VerifyPassword(currentPassword, currentPasswordHash)
                .Returns(true);

            _passwordHasher.VerifyPassword(invalidNewPassword, currentPasswordHash)
                .Returns(false);

            // Act
            var result = await _handler.HandleAsync(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.NotNull(result.Failure);
            Assert.Equal("InvalidPassword", result.Failure.ErrorCode);
            Assert.Equal("비밀번호는 최소 8자 이상이어야 하며, 영문 대/소문자, 숫자, 특수문자 중 3가지 이상을 조합해야 합니다.", result.Failure.Message);
        }
    }
}
