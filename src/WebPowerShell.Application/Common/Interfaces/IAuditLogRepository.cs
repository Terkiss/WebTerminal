using System.Threading;
using System.Threading.Tasks;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Application.Common.Interfaces
{
    public interface IAuditLogRepository
    {
        Task<Result<bool>> AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default);
    }
}
