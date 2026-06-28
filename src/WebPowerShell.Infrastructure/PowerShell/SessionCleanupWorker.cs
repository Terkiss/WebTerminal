using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebPowerShell.Application.Common.Interfaces;

namespace WebPowerShell.Infrastructure.PowerShell
{
    public class SessionCleanupWorker : BackgroundService
    {
        private readonly IPowerShellSessionService _sessionService;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<SessionCleanupWorker> _logger;
        private readonly TimeSpan _cleanupInterval;
        private readonly TimeSpan _idleTimeout;

        public SessionCleanupWorker(
            IPowerShellSessionService sessionService,
            TimeProvider timeProvider,
            ILogger<SessionCleanupWorker> logger,
            TimeSpan? cleanupInterval = null,
            TimeSpan? idleTimeout = null)
        {
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cleanupInterval = cleanupInterval ?? TimeSpan.FromMinutes(5);
            _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(30);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SessionCleanupWorker 백그라운드 서비스가 시작되었습니다.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 대기
                    await Task.Delay(_cleanupInterval, _timeProvider, stoppingToken);

                    _logger.LogDebug("유휴 세션 정리 작업을 시작합니다.");
                    
                    var result = await _sessionService.CleanIdleSessionsAsync(_idleTimeout, stoppingToken);
                    
                    if (result.IsSuccess)
                    {
                        if (result.Value > 0)
                        {
                            _logger.LogInformation("유휴 세션 {Count}개를 성공적으로 정리했습니다.", result.Value);
                        }
                    }
                    else
                    {
                        _logger.LogError("유휴 세션 정리 중 오류가 발생했습니다: {ErrorMessage}", result.Failure?.Message);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("SessionCleanupWorker 백그라운드 서비스가 취소 요청에 의해 종료됩니다.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SessionCleanupWorker에서 예외가 발생했습니다.");
                }
            }

            _logger.LogInformation("SessionCleanupWorker 백그라운드 서비스가 종료되었습니다.");
        }
    }
}
