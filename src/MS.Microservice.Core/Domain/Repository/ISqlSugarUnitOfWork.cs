using System.Threading.Tasks;

namespace MS.Microservice.Core.Domain.Repository
{
    public interface ISqlSugarUnitOfWork
    {
        Task BeginAsync();
        Task CommitAsync();
        Task RollbackAsync();
    }
}
