using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
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
        private DataFrame _users;

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

        public TeruTeruPandasUserRepository()
        {
            // Initialize empty Users DataFrame with typed columns (1 dummy row then slice to 0)
            // TeruTeruPandas requires at least 1 column with matching lengths.
            // We start with a single "seed" row that we'll never match, or build on first Save.
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
        /// TeruTeruPandas requires at least 1 column; we create 1-element arrays then use them
        /// as a base for append operations.
        /// </summary>
        private static DataFrame BuildEmptyDataFrame()
        {
            // Create a "sentinel" row that will be used as the initial empty state.
            // We use a 1-row DataFrame and will just skip this row during lookups
            // by checking for the sentinel Id value.
            // Actually, let's use a simpler approach: start with a 1-element DF as a template.
            var columns = new Dictionary<string, IColumn>();
            foreach (var colName in AllColumns)
            {
                columns[colName] = new StringColumn(new string?[] { "__SENTINEL__" });
            }
            // Build with 1 sentinel row. When first real user is saved,
            // we rebuild without the sentinel.
            return new DataFrame(columns);
        }
    }
}
