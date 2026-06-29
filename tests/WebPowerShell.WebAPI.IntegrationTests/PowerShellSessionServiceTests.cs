using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Common;
using WebPowerShell.Infrastructure.PowerShell;
using Xunit;

namespace WebPowerShell.WebAPI.IntegrationTests
{
    public class PowerShellSessionServiceTests
    {
        private readonly FakeTimeProvider _timeProvider;
        private readonly PowerShellSessionService _service;
        private readonly ILoggerFactory _loggerFactory;

        public PowerShellSessionServiceTests()
        {
            _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 29, 3, 0, 0, TimeSpan.Zero));
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());
            _service = new PowerShellSessionService(_timeProvider, Substitute.For<ILogger<PowerShellSessionService>>(), _loggerFactory);
        }

        [Fact]
        public async Task CreateSessionAsync_ShouldCreateNewSessionAndOpenRunspace()
        {
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();

            var result = await _service.CreateSessionAsync(userId, tabId, _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask);

            Assert.True(result.IsSuccess, result.Failure?.Message ?? "No error message");
            Assert.NotNull(result.Value);
            Assert.Equal(userId, result.Value.UserId);
            Assert.Equal(tabId, result.Value.TabId);

            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenSessionAlreadyExists_ShouldCloseAndReplaceIt()
        {
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();

            var firstResult = await _service.CreateSessionAsync(userId, tabId, _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask);
            Assert.True(firstResult.IsSuccess);
            var firstSessionId = firstResult.Value!.SessionId;

            var secondResult = await _service.CreateSessionAsync(userId, tabId, _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask);

            Assert.True(secondResult.IsSuccess);
            Assert.NotEqual(firstSessionId, secondResult.Value!.SessionId);

            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task GetSessionAsync_WhenSessionExists_ShouldReturnSession()
        {
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();
            await _service.CreateSessionAsync(userId, tabId, _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask);

            var result = await _service.GetSessionAsync(userId, tabId);

            Assert.True(result.IsSuccess);
            Assert.Equal(userId, result.Value!.UserId);
            Assert.Equal(tabId, result.Value!.TabId);

            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task GetSessionAsync_WhenSessionDoesNotExist_ShouldReturnSessionNotFound()
        {
            var result = await _service.GetSessionAsync(Guid.NewGuid(), Guid.NewGuid());
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.SessionNotFound.ErrorCode, result.Failure!.ErrorCode);
        }

        [Fact]
        public async Task WriteInputAsync_ShouldReturnSuccess()
        {
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();
            await _service.CreateSessionAsync(userId, tabId, _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask);

            var result = await _service.WriteInputAsync(userId, tabId, "echo 'hello'\n");

            Assert.True(result.IsSuccess);

            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task WriteInputAsync_WhenSessionNotFound_ShouldReturnSessionNotFound()
        {
            var result = await _service.WriteInputAsync(Guid.NewGuid(), Guid.NewGuid(), "echo 'hello'\n");
            Assert.True(result.IsFailure);
            Assert.Equal(AppFailure.SessionNotFound.ErrorCode, result.Failure!.ErrorCode);
        }

        [Fact]
        public async Task StopCommandAsync_ShouldReturnSuccess()
        {
            var userId = Guid.NewGuid();
            var tabId = Guid.NewGuid();
            await _service.CreateSessionAsync(userId, tabId, _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask);

            var stopResult = await _service.StopCommandAsync(userId, tabId);
            Assert.True(stopResult.IsSuccess);

            await _service.CloseSessionAsync(userId, tabId);
        }

        [Fact]
        public async Task CleanIdleSessionsAsync_ShouldCleanExpiredSessionsOnly()
        {
            var user1 = Guid.NewGuid();
            var tab1 = Guid.NewGuid();
            var user2 = Guid.NewGuid();
            var tab2 = Guid.NewGuid();

            var s1Result = await _service.CreateSessionAsync(user1, tab1, _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask);
            Assert.True(s1Result.IsSuccess);

            _timeProvider.Advance(TimeSpan.FromMinutes(15));

            var s2Result = await _service.CreateSessionAsync(user2, tab2, _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask);
            Assert.True(s2Result.IsSuccess);

            _timeProvider.Advance(TimeSpan.FromMinutes(20));

            var cleanResult = await _service.CleanIdleSessionsAsync(TimeSpan.FromMinutes(30));

            Assert.True(cleanResult.IsSuccess);
            Assert.Equal(1, cleanResult.Value);

            var getS1 = await _service.GetSessionAsync(user1, tab1);
            Assert.True(getS1.IsFailure);
            Assert.Equal(AppFailure.SessionNotFound.ErrorCode, getS1.Failure!.ErrorCode);

            var getS2 = await _service.GetSessionAsync(user2, tab2);
            Assert.True(getS2.IsSuccess);

            await _service.CloseSessionAsync(user2, tab2);
        }

        [Fact]
        public void Dispose_ShouldBeIdempotent_WhenCalledMultipleTimes()
        {
            _service.Dispose();
            var ex = Record.Exception(() => _service.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public async Task DisposeAsync_ShouldBeIdempotent_WhenCalledMultipleTimes()
        {
            await _service.DisposeAsync();
            var ex = await Record.ExceptionAsync(async () => await _service.DisposeAsync());
            Assert.Null(ex);
        }

        [Fact]
        public async Task APICalls_AfterDispose_ShouldThrowObjectDisposedException()
        {
            _service.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => _service.CreateSessionAsync(Guid.NewGuid(), Guid.NewGuid(), _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => _service.WriteInputAsync(Guid.NewGuid(), Guid.NewGuid(), "Test"));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => _service.StopCommandAsync(Guid.NewGuid(), Guid.NewGuid()));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => _service.CloseSessionAsync(Guid.NewGuid(), Guid.NewGuid()));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => _service.GetSessionAsync(Guid.NewGuid(), Guid.NewGuid()));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => _service.CleanIdleSessionsAsync(TimeSpan.FromMinutes(1)));
        }
    }
}
