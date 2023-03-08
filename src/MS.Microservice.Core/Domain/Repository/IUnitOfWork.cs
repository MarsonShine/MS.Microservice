using System;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Domain.Repository
{
    public interface IUnitOfWork: IDisposable
    {
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
        Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default);
    }
}
