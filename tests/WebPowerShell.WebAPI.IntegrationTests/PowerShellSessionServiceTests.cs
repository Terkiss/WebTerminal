using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Sessions.Common;
using WebPowerShell.Domain.Common;
using WebPowerShell.Infrastructure.PowerShell;
using Xunit;

namespace WebPowerShell.WebAPI.IntegrationTests
{
    public class PowerShellSessionServiceTests
    {
        private readonly FakeTimeProvider _timeProvider;
        private readonly PowerShellSessionService _service;

        public PowerShellSessionServiceTests()
        {
            _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 29, 3, 0, 0, TimeSpan.Zero));
            _service = new PowerShellSessionService(_timeProvider, Substitute.For<ILogger<PowerShellSessionService>>());
        }

        [Fact]
        public async Task CreateSessionAsync_ShouldCreateNewSessionAndOpenRunspace()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();

            // Act
            var result = await _service.CreateSessionAsync(userId, tabId);

            // Assert
            Assert.True(result.IsSuccess, result.Failure?.Message ?? "No error message");
            Assert.NotNull(result.Value);
            Assert.Equal(userId, result.Value.UserId);
            Assert.Equal(tabId, result.Value.TabId);
            Assert.Equal(_timeProvider.GetUtcNow(), result.Value.CreatedAt);
            Assert.Equal(_timeProvider.GetUtcNow(), result.Value.LastActiveAt);

            // Clean up
            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenSessionAlreadyExists_ShouldCloseAndReplaceIt()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();

            // First session
            var firstResult = await _service.CreateSessionAsync(userId, tabId);
            Assert.True(firstResult.IsSuccess);
            var firstSessionId = firstResult.Value!.SessionId;

            // Act
            var secondResult = await _service.CreateSessionAsync(userId, tabId);

            // Assert
            Assert.True(secondResult.IsSuccess);
            Assert.NotEqual(firstSessionId, secondResult.Value!.SessionId);

            // Clean up
            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task GetSessionAsync_WhenSessionExists_ShouldReturnSession()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();
            await _service.CreateSessionAsync(userId, tabId);

            // Act
            var result = await _service.GetSessionAsync(userId, tabId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(userId, result.Value!.UserId);
            Assert.Equal(tabId, result.Value!.TabId);

            // Clean up
            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task GetSessionAsync_WhenSessionDoesNotExist_ShouldReturnSessionNotFound()
        {
            // Act
            var result = await _service.GetSessionAsync(Guid.NewGuid(), Guid.NewGuid());

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.SessionNotFound.ErrorCode, result.Failure!.ErrorCode);
        }

        [Fact]
        public async Task CloseSessionAsync_WhenSessionDoesNotExist_ShouldReturnSessionNotFound()
        {
            // Act
            var result = await _service.CloseSessionAsync(Guid.NewGuid(), Guid.NewGuid());

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.SessionNotFound.ErrorCode, result.Failure!.ErrorCode);
        }

        [Fact]
        public async Task ExecuteCommandAsync_ShouldAcquireLockAndReturnSuccess()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();
            await _service.CreateSessionAsync(userId, tabId);

            // Act
            var result = await _service.ExecuteCommandAsync(
                userId, 
                tabId, 
                "Get-Process", 
                (streamData, ct) => Task.CompletedTask
            );

            // Assert
            Assert.True(result.IsSuccess);

            // Clean up
            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task ExecuteCommandAsync_WhenSessionNotFound_ShouldReturnSessionNotFound()
        {
            // Act
            var result = await _service.ExecuteCommandAsync(
                Guid.NewGuid(), 
                Guid.NewGuid(), 
                "Get-Process", 
                (streamData, ct) => Task.CompletedTask
            );

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.SessionNotFound.ErrorCode, result.Failure!.ErrorCode);
        }

        [Fact]
        public async Task ExecuteCommandAsync_SessionBusy_ReturnsSessionBusy()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();
            await _service.CreateSessionAsync(userId, tabId);

            var firstCallStarted = new TaskCompletionSource<bool>();
            var firstCallHold = new TaskCompletionSource<bool>();

            // 첫 번째 명령어 실행 (onStream 콜백 내에서 대기하게 만듬)
            var firstCallTask = _service.ExecuteCommandAsync(
                userId,
                tabId,
                "Long-Running-Command",
                async (streamData, ct) =>
                {
                    firstCallStarted.SetResult(true);
                    await firstCallHold.Task; // 두 번째 호출이 진행될 때까지 대기
                }
            );

            // 첫 번째 호출이 onStream 내부로 들어갈 때까지 대기
            await firstCallStarted.Task;

            // Act: 동일 세션에 두 번째 명령어 호출 (락 획득 실패해야 함)
            var secondResult = await _service.ExecuteCommandAsync(
                userId,
                tabId,
                "Another-Command",
                (streamData, ct) => Task.CompletedTask
            );

            // 첫 번째 명령어 대기 해제 및 종료 대기
            firstCallHold.SetResult(true);
            await firstCallTask;

            // Assert
            Assert.True(secondResult.IsFailure);
            Assert.Equal(AppFailure.SessionBusy.ErrorCode, secondResult.Failure!.ErrorCode);

            // Clean up
            await _service.CloseSessionAsync(userId, tabId);
        }
    }
}
