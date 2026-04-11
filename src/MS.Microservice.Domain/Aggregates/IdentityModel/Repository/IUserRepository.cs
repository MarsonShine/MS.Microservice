using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Functional;
using System.Linq.Expressions;

namespace MS.Microservice.Domain.Aggregates.IdentityModel.Repository
{
    public interface IUserRepository : IRepository<User>
    {
        Task<User?> GetAsync(int userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 函数式版本的查询接口：用 Option 替代裸 null，显式表达“可能不存在”。
        /// </summary>
        Task<Option<User>> FindOptionAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default);

        Task<User?> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default);
        Task<Result<User>> InsertResultAsync(User user, CancellationToken cancellationToken = default);
        Task<User> InsertAsync(User user, CancellationToken cancellationToken = default);
        Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);
        Task<List<Role>> GetAllRoleAsync(CancellationToken cancellationToken = default);
    }
}
