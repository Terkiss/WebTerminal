using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.IO;
using WebPowerShell.Infrastructure.ConPTY;
using WebPowerShell.Infrastructure.Persistence.Repositories;

namespace WebPowerShell.Infrastructure.Persistence
{
    /// <summary>
    /// Background service that periodically persists in-memory data (users + sessions)
    /// to a SQLite file on disk. Runs every 120 seconds and only writes when data has changed.
    /// On startup, restores data from the SQLite file if it exists.
    /// 
    /// Storage path: {ExecutablePath}/MEMORY/memory.db
    /// </summary>
    public class MemoryPersistenceService : BackgroundService
    {
        private readonly TeruTeruPandasUserRepository _userRepo;
        private readonly ITerminalSessionManager _sessionManager;
        private readonly ILogger<MemoryPersistenceService> _logger;
        private readonly string _dbPath;
        private const int PersistIntervalSeconds = 120;

        // Session record columns
        private static readonly string[] SessionColumns = { "SessionId", "OwnerUserId", "WorkingDirectory", "CreatedAt" };

        public MemoryPersistenceService(
            TeruTeruPandasUserRepository userRepo,
            ITerminalSessionManager sessionManager,
            ILogger<MemoryPersistenceService> logger)
        {
            _userRepo = userRepo;
            _sessionManager = sessionManager;
            _logger = logger;

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
            // Session records are loaded lazily via GetPersistedSessions()
            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Main loop: every 120 seconds, persist dirty data.
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

                PersistAll();
            }
        }

        /// <summary>
        /// On shutdown, persist all data to ensure nothing is lost.
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[MemoryPersistence] Shutting down — final persistence...");
            PersistAll();
            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Persist both users and live session records.
        /// </summary>
        private void PersistAll()
        {
            try
            {
                // Always persist users if dirty
                if (_userRepo.IsDirty)
                {
                    _userRepo.PersistToSqlite(_dbPath);
                }

                // Always persist current session snapshot
                PersistSessions();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MemoryPersistence] PersistAll failed");
            }
        }

        /// <summary>
        /// Persist live terminal session metadata to the sessions table in SQLite.
        /// </summary>
        private void PersistSessions()
        {
            try
            {
                var sessions = _sessionManager.GetAllSessions();
                if (sessions.Count == 0)
                {
                    // Delete sessions table if no live sessions
                    if (File.Exists(_dbPath))
                    {
                        var connStr = $"Data Source={_dbPath}";
                        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
                        conn.Open();
                        using var cmd = new Microsoft.Data.Sqlite.SqliteCommand("DROP TABLE IF EXISTS sessions", conn);
                        cmd.ExecuteNonQuery();
                    }
                    _logger.LogDebug("[MemoryPersistence] No live sessions to persist.");
                    return;
                }

                var dir = Path.GetDirectoryName(_dbPath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Build DataFrame from live sessions
                var sessionIds = new string?[sessions.Count];
                var ownerIds = new string?[sessions.Count];
                var workDirs = new string?[sessions.Count];
                var createdAts = new string?[sessions.Count];

                for (int i = 0; i < sessions.Count; i++)
                {
                    sessionIds[i] = sessions[i].SessionId.ToString();
                    ownerIds[i] = sessions[i].OwnerUserId.ToString();
                    workDirs[i] = sessions[i].WorkingDirectory;
                    createdAts[i] = sessions[i].CreatedAt.ToString("O");
                }

                var columns = new Dictionary<string, IColumn>
                {
                    ["SessionId"] = new StringColumn(sessionIds),
                    ["OwnerUserId"] = new StringColumn(ownerIds),
                    ["WorkingDirectory"] = new StringColumn(workDirs),
                    ["CreatedAt"] = new StringColumn(createdAts),
                };
                var df = new DataFrame(columns);

                var connString = $"Data Source={_dbPath}";
                SqliteIO.ToSqlite(df, connString, "sessions", ifExists: true);

                _logger.LogInformation("[MemoryPersistence] Persisted {Count} session records.", sessions.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MemoryPersistence] Failed to persist sessions");
            }
        }

        /// <summary>
        /// Load persisted session records from SQLite.
        /// Returns a list of (SessionId, OwnerUserId, WorkingDirectory, CreatedAt) tuples.
        /// Called by TerminalHub when a user connects to restore their sessions.
        /// </summary>
        public List<(Guid SessionId, Guid OwnerUserId, string WorkingDirectory, DateTimeOffset CreatedAt)> GetPersistedSessions()
        {
            var result = new List<(Guid, Guid, string, DateTimeOffset)>();

            if (!File.Exists(_dbPath))
                return result;

            try
            {
                var tables = SqliteIO.GetTableNames(_dbPath);
                if (!tables.Contains("sessions"))
                    return result;

                var df = SqliteIO.ReadSqliteTable(_dbPath, "sessions");
                for (int i = 0; i < df.RowCount; i++)
                {
                    var sessionId = Guid.TryParse(df[i, "SessionId"]?.ToString(), out var sid) ? sid : Guid.Empty;
                    var ownerId = Guid.TryParse(df[i, "OwnerUserId"]?.ToString(), out var oid) ? oid : Guid.Empty;
                    var workDir = df[i, "WorkingDirectory"]?.ToString() ?? "";
                    var createdAt = DateTimeOffset.TryParse(df[i, "CreatedAt"]?.ToString(), out var ca) ? ca : DateTimeOffset.UtcNow;

                    if (sessionId != Guid.Empty && ownerId != Guid.Empty)
                    {
                        result.Add((sessionId, ownerId, workDir, createdAt));
                    }
                }

                _logger.LogInformation("[MemoryPersistence] Loaded {Count} persisted session records.", result.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MemoryPersistence] Failed to load session records");
            }

            return result;
        }
    }
}
