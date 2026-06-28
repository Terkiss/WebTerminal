using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Infrastructure.Persistence.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _context;

        public UserRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Result<User>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

                if (user == null)
                {
                    return Result<User>.Fail(AppFailure.Unauthorized);
                }

                return Result<User>.Success(user);
            }
            catch (Exception ex)
            {
                return Result<User>.Fail(new AppFailure("DatabaseError", ex.Message));
            }
        }

        public async Task<Result<User>> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower(), cancellationToken);

                if (user == null)
                {
                    return Result<User>.Fail(AppFailure.Unauthorized);
                }

                return Result<User>.Success(user);
            }
            catch (Exception ex)
            {
                return Result<User>.Fail(new AppFailure("DatabaseError", ex.Message));
            }
        }

        public async Task<Result<bool>> SaveAsync(User user, CancellationToken cancellationToken = default)
        {
            try
            {
                var exists = await _context.Users.AnyAsync(u => u.Id == user.Id, cancellationToken);
                if (exists)
                {
                    _context.Users.Update(user);
                }
                else
                {
                    await _context.Users.AddAsync(user, cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail(new AppFailure("DatabaseError", ex.Message));
            }
        }
    }
}
