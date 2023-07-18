using MS.Microservice.Core.Domain.Repository;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.Aggregates.IdentityModel.Repository
{
    public interface IUserRepository : IRepository<User>
    {
        Task<User?> GetAsync(int userId, CancellationToken cancellationToken = default);
        Task<User?> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default);
        Task<User> InsertAsync(User user, CancellationToken cancellationToken = default);
        Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);

        Task<List<Role>> GetAllRoleAsync(CancellationToken cancellationToken = default);

    }
}
