using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using TeruTeruPandas.IO;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// IUserRepository implementation backed by TeruTeruPandas DataFrame (in-memory).
    /// Thread-safe via ReaderWriterLockSlim for concurrent web request access.
    /// Registered as Singleton in DI — data persists for the lifetime of the application.
    /// </summary>
    public class TeruTeruPandasUserRepository : IUserRepository
    {
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly ILogger<TeruTeruPandasUserRepository>? _logger;
        private DataFrame _users;
        private volatile bool _isDirty;

        /// <summary>
        /// Indicates whether the in-memory data has been modified since the last persistence.
        /// </summary>
        public bool IsDirty => _isDirty;

        // Column name constants
        private const string COL_ID = "Id";
        private const string COL_USERNAME = "Username";
        private const string COL_PASSWORD_HASH = "PasswordHash";
        private const string COL_LAST_PW_CHANGE = "LastPasswordChangeDate";
        private const string COL_IS_ACTIVE = "IsActive";
        private const string COL_CREATED_AT = "CreatedAt";
        private const string COL_UPDATED_AT = "UpdatedAt";
        private const string COL_FAILED_LOGIN = "FailedLoginCount";
        private const string COL_LOCKED_UNTIL = "LockedUntil";
        private const string COL_IS_ADMIN = "IsAdmin";

        private static readonly string[] AllColumns = new[]
        {
            COL_ID, COL_USERNAME, COL_PASSWORD_HASH, COL_LAST_PW_CHANGE,
            COL_IS_ACTIVE, COL_CREATED_AT, COL_UPDATED_AT,
            COL_FAILED_LOGIN, COL_LOCKED_UNTIL, COL_IS_ADMIN
        };

        public TeruTeruPandasUserRepository(ILogger<TeruTeruPandasUserRepository>? logger = null)
        {
            _logger = logger;
            _users = BuildEmptyDataFrame();
        }

        public Task<Result<User>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _lock.EnterReadLock();
            try
            {
                string idStr = id.ToString();
                for (int i = 0; i < _users.RowCount; i++)
                {
                    var val = _users[i, COL_ID]?.ToString();
                    if (string.Equals(val, idStr, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(Result<User>.Success(RowToUser(i)));
                    }
                }
                return Task.FromResult(Result<User>.Fail(AppFailure.Unauthorized));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<User>.Fail(new AppFailure("DataFrameError", ex.Message)));
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public Task<Result<User>> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            _lock.EnterReadLock();
            try
            {
                for (int i = 0; i < _users.RowCount; i++)
                {
                    var val = _users[i, COL_USERNAME]?.ToString();
                    if (string.Equals(val, username, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(Result<User>.Success(RowToUser(i)));
                    }
                }
                return Task.FromResult(Result<User>.Fail(AppFailure.Unauthorized));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<User>.Fail(new AppFailure("DataFrameError", ex.Message)));
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public Task<Result<bool>> SaveAsync(User user, CancellationToken cancellationToken = default)
        {
            _lock.EnterWriteLock();
            try
            {
                string idStr = user.Id.ToString();
                int existingRow = -1;

                for (int i = 0; i < _users.RowCount; i++)
                {
                    if (string.Equals(_users[i, COL_ID]?.ToString(), idStr, StringComparison.OrdinalIgnoreCase))
                    {
                        existingRow = i;
                        break;
                    }
                }

                if (existingRow >= 0)
                {
                    // Update existing row via DataFrame[row, col] setter
                    _users[existingRow, COL_ID]             = user.Id.ToString();
                    _users[existingRow, COL_USERNAME]        = user.Username;
                    _users[existingRow, COL_PASSWORD_HASH]   = user.PasswordHash;
                    _users[existingRow, COL_LAST_PW_CHANGE]  = user.LastPasswordChangeDate.ToString("O");
                    _users[existingRow, COL_IS_ACTIVE]       = user.IsActive.ToString();
                    _users[existingRow, COL_CREATED_AT]      = user.CreatedAt.ToString("O");
                    _users[existingRow, COL_UPDATED_AT]      = user.UpdatedAt.ToString("O");
                    _users[existingRow, COL_FAILED_LOGIN]    = user.FailedLoginCount.ToString();
                    _users[existingRow, COL_LOCKED_UNTIL]    = user.LockedUntil?.ToString("O") ?? "";
                    _users[existingRow, COL_IS_ADMIN]        = user.IsAdmin.ToString();
                }
                else
                {
                    // Append new row by rebuilding DataFrame with one extra row
                    int oldCount = _users.RowCount;
                    int newCount = oldCount + 1;

                    var newColumns = new Dictionary<string, IColumn>();
                    foreach (var colName in AllColumns)
                    {
                        var values = new string?[newCount];
                        // Copy existing data
                        for (int i = 0; i < oldCount; i++)
                        {
                            values[i] = _users[i, colName]?.ToString() ?? "";
                        }
                        // Set new row value
                        values[oldCount] = colName switch
                        {
                            COL_ID             => user.Id.ToString(),
                            COL_USERNAME       => user.Username,
                            COL_PASSWORD_HASH  => user.PasswordHash,
                            COL_LAST_PW_CHANGE => user.LastPasswordChangeDate.ToString("O"),
                            COL_IS_ACTIVE      => user.IsActive.ToString(),
                            COL_CREATED_AT     => user.CreatedAt.ToString("O"),
                            COL_UPDATED_AT     => user.UpdatedAt.ToString("O"),
                            COL_FAILED_LOGIN   => user.FailedLoginCount.ToString(),
                            COL_LOCKED_UNTIL   => user.LockedUntil?.ToString("O") ?? "",
                            COL_IS_ADMIN       => user.IsAdmin.ToString(),
                            _                  => ""
                        };
                        newColumns[colName] = new StringColumn(values);
                    }

                    _users = new DataFrame(newColumns);
                }

                _isDirty = true;
                return Task.FromResult(Result<bool>.Success(true));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<bool>.Fail(new AppFailure("DataFrameError", ex.Message)));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Convert a DataFrame row index to a User domain entity.
        /// </summary>
        private User RowToUser(int rowIndex)
        {
            return new User
            {
                Id                     = Guid.Parse(_users[rowIndex, COL_ID]?.ToString() ?? Guid.Empty.ToString()),
                Username               = _users[rowIndex, COL_USERNAME]?.ToString() ?? "",
                PasswordHash           = _users[rowIndex, COL_PASSWORD_HASH]?.ToString() ?? "",
                LastPasswordChangeDate = DateTimeOffset.TryParse(_users[rowIndex, COL_LAST_PW_CHANGE]?.ToString(), out var lpcd) ? lpcd : DateTimeOffset.UtcNow,
                IsActive               = bool.TryParse(_users[rowIndex, COL_IS_ACTIVE]?.ToString(), out var ia) && ia,
                CreatedAt              = DateTimeOffset.TryParse(_users[rowIndex, COL_CREATED_AT]?.ToString(), out var ca) ? ca : DateTimeOffset.UtcNow,
                UpdatedAt              = DateTimeOffset.TryParse(_users[rowIndex, COL_UPDATED_AT]?.ToString(), out var ua) ? ua : DateTimeOffset.UtcNow,
                FailedLoginCount       = int.TryParse(_users[rowIndex, COL_FAILED_LOGIN]?.ToString(), out var flc) ? flc : 0,
                LockedUntil            = DateTimeOffset.TryParse(_users[rowIndex, COL_LOCKED_UNTIL]?.ToString(), out var lu) ? lu : null,
                IsAdmin                = bool.TryParse(_users[rowIndex, COL_IS_ADMIN]?.ToString(), out var isa) && isa,
            };
        }

        /// <summary>
        /// Build a DataFrame with 0 rows but all required columns.
        /// </summary>
        private static DataFrame BuildEmptyDataFrame()
        {
            var columns = new Dictionary<string, IColumn>();
            foreach (var colName in AllColumns)
            {
                columns[colName] = new StringColumn(new string?[] { "__SENTINEL__" });
            }
            return new DataFrame(columns);
        }

        /// <summary>
        /// Persist the current in-memory DataFrame to a SQLite database file.
        /// Called by MemoryPersistenceService on a 120-second interval.
        /// </summary>
        public void PersistToSqlite(string dbPath)
        {
            _lock.EnterReadLock();
            try
            {
                // Only persist real user rows (skip sentinel)
                var realRowCount = 0;
                for (int i = 0; i < _users.RowCount; i++)
                {
                    if (_users[i, COL_ID]?.ToString() != "__SENTINEL__")
                        realRowCount++;
                }

                if (realRowCount == 0)
                {
                    _logger?.LogInformation("[MemoryPersistence] No real user data to persist.");
                    return;
                }

                // Build a clean DataFrame without sentinel rows
                var cleanColumns = new Dictionary<string, IColumn>();
                foreach (var colName in AllColumns)
                {
                    var values = new string?[realRowCount];
                    int idx = 0;
                    for (int i = 0; i < _users.RowCount; i++)
                    {
                        if (_users[i, COL_ID]?.ToString() != "__SENTINEL__")
                        {
                            values[idx++] = _users[i, colName]?.ToString() ?? "";
                        }
                    }
                    cleanColumns[colName] = new StringColumn(values);
                }
                var cleanDf = new DataFrame(cleanColumns);

                var dir = Path.GetDirectoryName(dbPath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var connStr = $"Data Source={dbPath}";
                SqliteIO.ToSqlite(cleanDf, connStr, "users", ifExists: true);

                _isDirty = false;
                _logger?.LogInformation("[MemoryPersistence] Persisted {Count} users to {Path}", realRowCount, dbPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[MemoryPersistence] Failed to persist to SQLite");
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Load user data from a SQLite database file into the in-memory DataFrame.
        /// Called once on application startup.
        /// </summary>
        public void LoadFromSqlite(string dbPath)
        {
            if (!File.Exists(dbPath))
            {
                _logger?.LogInformation("[MemoryPersistence] No existing DB found at {Path}, starting fresh.", dbPath);
                return;
            }

            _lock.EnterWriteLock();
            try
            {
                var tables = SqliteIO.GetTableNames(dbPath);
                if (!tables.Contains("users"))
                {
                    _logger?.LogWarning("[MemoryPersistence] DB exists but no 'users' table found.");
                    return;
                }

                var loaded = SqliteIO.ReadSqliteTable(dbPath, "users");
                if (loaded.RowCount > 0)
                {
                    // Rebuild as StringColumn-based DataFrame for consistency
                    var columns = new Dictionary<string, IColumn>();
                    foreach (var colName in AllColumns)
                    {
                        if (loaded.Columns.Contains(colName))
                        {
                            var values = new string?[loaded.RowCount];
                            for (int i = 0; i < loaded.RowCount; i++)
                            {
                                values[i] = loaded[i, colName]?.ToString() ?? "";
                            }
                            columns[colName] = new StringColumn(values);
                        }
                        else
                        {
                            // Column missing in DB, fill with defaults
                            var values = new string?[loaded.RowCount];
                            Array.Fill(values, "");
                            columns[colName] = new StringColumn(values);
                        }
                    }
                    _users = new DataFrame(columns);
                    _isDirty = false;
                    _logger?.LogInformation("[MemoryPersistence] Loaded {Count} users from {Path}", loaded.RowCount, dbPath);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[MemoryPersistence] Failed to load from SQLite");
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Clear the dirty flag after a successful persistence.
        /// </summary>
        public void ClearDirtyFlag() => _isDirty = false;
    }
}
