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

        [Fact]
        public async Task ExecuteCommandAsync_ShouldStreamStandardOutput()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();
            await _service.CreateSessionAsync(userId, tabId);

            var outputReceived = new TaskCompletionSource<string>();

            // Act
            var result = await _service.ExecuteCommandAsync(
                userId,
                tabId,
                "Write-Output 'TestMessage'",
                (streamData, ct) =>
                {
                    if (streamData.Type == PowerShellStreamType.Output)
                    {
                        outputReceived.TrySetResult(streamData.Content);
                    }
                    return Task.CompletedTask;
                }
            );

            // Assert
            Assert.True(result.IsSuccess);

            var completedTask = await Task.WhenAny(outputReceived.Task, Task.Delay(5000));
            Assert.Same(outputReceived.Task, completedTask);

            var content = await outputReceived.Task;
            Assert.Equal("TestMessage", content);

            // Clean up
            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task ExecuteCommandAsync_ShouldStreamErrorOutput()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();
            await _service.CreateSessionAsync(userId, tabId);

            var errorReceived = new TaskCompletionSource<string>();

            // Act
            var result = await _service.ExecuteCommandAsync(
                userId,
                tabId,
                "Write-Error 'TestError'",
                (streamData, ct) =>
                {
                    if (streamData.Type == PowerShellStreamType.Error)
                    {
                        errorReceived.TrySetResult(streamData.Content);
                    }
                    return Task.CompletedTask;
                }
            );

            // Assert
            Assert.True(result.IsSuccess);

            var completedTask = await Task.WhenAny(errorReceived.Task, Task.Delay(5000));
            Assert.Same(errorReceived.Task, completedTask);

            var content = await errorReceived.Task;
            Assert.Contains("TestError", content);

            // Clean up
            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task StopCommandAsync_ShouldStopRunningCommandAndReleaseLock()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();
            await _service.CreateSessionAsync(userId, tabId);

            var commandStarted = new TaskCompletionSource<bool>();

            // Act
            var runTask = _service.ExecuteCommandAsync(
                userId,
                tabId,
                "Write-Output 'Started'; Start-Sleep -Seconds 10",
                (streamData, ct) =>
                {
                    if (streamData.Type == PowerShellStreamType.Output && streamData.Content == "Started")
                    {
                        commandStarted.TrySetResult(true);
                    }
                    return Task.CompletedTask;
                }
            );

            // 명령어가 시작될 때까지 최대 5초 대기
            var startCompleted = await Task.WhenAny(commandStarted.Task, Task.Delay(5000));
            Assert.Same(commandStarted.Task, startCompleted);
            Assert.True(await commandStarted.Task);

            // 명령어 실행 중 중지 호출
            var stopResult = await _service.StopCommandAsync(userId, tabId);
            Assert.True(stopResult.IsSuccess);

            // 중단 호출 후 runTask가 완료되는지 대기 (타임아웃 5초)
            var runCompleted = await Task.WhenAny(runTask, Task.Delay(5000));
            Assert.Same(runTask, runCompleted);

            await runTask;

            // Assert: 락이 해제되어 새로운 명령을 즉시 다시 실행할 수 있는지 검증
            var nextResult = await _service.ExecuteCommandAsync(
                userId,
                tabId,
                "Write-Output 'Next'",
                (streamData, ct) => Task.CompletedTask
            );
            Assert.True(nextResult.IsSuccess, "락이 해제되지 않아 다음 명령 실행에 실패했습니다.");

            // Clean up
            await _service.CloseSessionAsync(userId, tabId);
        }
    }
}
