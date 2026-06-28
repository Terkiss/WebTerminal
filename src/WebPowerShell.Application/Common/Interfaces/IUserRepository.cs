using System;
using System.Threading;
using System.Threading.Tasks;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Application.Common.Interfaces
{
    public interface IUserRepository
    {
        Task<Result<User>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Result<User>> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
        Task<Result<bool>> SaveAsync(User user, CancellationToken cancellationToken = default);
    }
}
