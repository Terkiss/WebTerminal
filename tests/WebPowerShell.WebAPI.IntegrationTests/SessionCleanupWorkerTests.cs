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
    public class SessionCleanupWorkerTests
    {
        [Fact]
        public async Task ExecuteAsync_ShouldCallCleanIdleSessionsAsyncPeriodically()
        {
            // Arrange
            var sessionServiceMock = Substitute.For<IPowerShellSessionService>();
            var loggerMock = Substitute.For<ILogger<SessionCleanupWorker>>();
            var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 6, 29, 3, 0, 0, TimeSpan.Zero));

            var cleanCalledTcs = new TaskCompletionSource<bool>();

            sessionServiceMock.CleanIdleSessionsAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
                .Returns(x => {
                    cleanCalledTcs.TrySetResult(true);
                    return Task.FromResult(Result<int>.Success(0));
                });

            // 10밀리초 주기로 세션 클린이 돌도록 주입
            var worker = new SessionCleanupWorker(
                sessionServiceMock, 
                timeProvider, 
                loggerMock, 
                cleanupInterval: TimeSpan.FromMilliseconds(10)
            );

            // Act
            using var cts = new CancellationTokenSource();
            var startTask = worker.StartAsync(cts.Token);

            // CleanIdleSessionsAsync가 주기적으로 불려서 cleanCalledTcs가 완료될 때까지 대기
            var completedTask = await Task.WhenAny(cleanCalledTcs.Task, Task.Delay(3000));
            Assert.Same(cleanCalledTcs.Task, completedTask);

            // cts를 취소해서 루프 종료
            cts.Cancel();
            await worker.StopAsync(CancellationToken.None);

            // Assert
            await sessionServiceMock.Received().CleanIdleSessionsAsync(
                TimeSpan.FromMinutes(30), 
                Arg.Any<CancellationToken>()
            );
        }
    }
}
