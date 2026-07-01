using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebPowerShell.Infrastructure.Persistence.Repositories;

namespace WebPowerShell.Infrastructure.Persistence
{
    /// <summary>
    /// Background service that periodically persists in-memory user data (TeruTeruPandas DataFrame)
    /// to a SQLite file on disk. Runs every 120 seconds and only writes when data has changed.
    /// On startup, restores data from the SQLite file if it exists.
    /// 
    /// Storage path: {ExecutablePath}/MEMORY/memory.db
    /// </summary>
    public class MemoryPersistenceService : BackgroundService
    {
        private readonly TeruTeruPandasUserRepository _userRepo;
        private readonly ILogger<MemoryPersistenceService> _logger;
        private readonly string _dbPath;
        private const int PersistIntervalSeconds = 120;

        public MemoryPersistenceService(
            TeruTeruPandasUserRepository userRepo,
            ILogger<MemoryPersistenceService> logger)
        {
            _userRepo = userRepo;
            _logger = logger;

            // Resolve executable directory for MEMORY/memory.db path
            var exeDir = AppContext.BaseDirectory;
            var memoryDir = Path.Combine(exeDir, "MEMORY");
            _dbPath = Path.Combine(memoryDir, "memory.db");

            _logger.LogInformation("[MemoryPersistence] DB path: {Path}", _dbPath);
        }

        /// <summary>
        /// On startup, load existing data from SQLite if available.
        /// </summary>
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[MemoryPersistence] Starting — loading persisted data...");
            _userRepo.LoadFromSqlite(_dbPath);
            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Main loop: every 120 seconds, check if data is dirty and persist if needed.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[MemoryPersistence] Periodic persistence started (interval: {Sec}s)", PersistIntervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(PersistIntervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (_userRepo.IsDirty)
                {
                    _logger.LogInformation("[MemoryPersistence] Change detected — persisting to disk...");
                    _userRepo.PersistToSqlite(_dbPath);
                }
            }
        }

        /// <summary>
        /// On shutdown, persist any remaining dirty data to ensure nothing is lost.
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[MemoryPersistence] Shutting down — final persistence...");
            if (_userRepo.IsDirty)
            {
                _userRepo.PersistToSqlite(_dbPath);
            }
            return base.StopAsync(cancellationToken);
        }
    }
}
