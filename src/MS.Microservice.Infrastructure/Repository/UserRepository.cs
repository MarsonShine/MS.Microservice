using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;
using MS.Microservice.Infrastructure.DbContext;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace MS.Microservice.Infrastructure.Repository
{
    public class UserRepository : BasicRepositoryBase<User>, IUserRepository
    {
        private readonly ActivationDbContext _dbContext;
        public UserRepository([NotNull] ActivationDbContext dbContext) : base(dbContext)
        {
            _dbContext = dbContext;
        }

        public override Task<bool> DeleteAsync([NotNull] Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override Task<bool> DeleteAsync([NotNull] User entity, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override async Task<User?> FindAsync([NotNull] Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Users
                .Include(u => u.Roles)
                    .ThenInclude(r => r.Actions)
                .Where(predicate)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// 供函数式领域服务使用，把数据库缺失结果安全地提升为 Option。
        /// </summary>
        public async Task<Option<User>> FindOptionAsync([NotNull] Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
            => await FindAsync(predicate, cancellationToken);

        public Task<Either<Error, User>> InsertEitherAsync([NotNull] User entity, CancellationToken cancellationToken = default)
            => EitherExtensions.TryAsync(() => InsertAsync(entity, cancellationToken), code: "user.insert");

        public Task<Result<User>> InsertResultAsync([NotNull] User entity, CancellationToken cancellationToken = default)
            => ResultExtensions.TryAsync(() => InsertAsync(entity, cancellationToken));

        public async Task<List<Role>> GetAllRoleAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Roles.AsNoTracking().ToListAsync(cancellationToken);
        }

        public async Task<User?> GetAsync(int userId, CancellationToken cancellationToken = default)
        {
            return await FindAsync(p => p.Id == userId, cancellationToken);
        }

        public override async Task<User> InsertAsync([NotNull] User entity, CancellationToken cancellationToken = default)
        {
            var roleIds = entity.Roles.Select(p => p.Id).ToArray();
            var existingRole = await _dbContext.Roles
                .Where(p => roleIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            entity.Roles.Clear();
            entity.Roles.AddIfNotContains(existingRole);

            var user = await _dbContext.Users.AddAsync(entity, cancellationToken);
            return user.Entity;
        }

        public override async Task<User> UpdateAsync([NotNull] User entity, CancellationToken cancellationToken = default)
        {
            _dbContext.Entry(entity).State = EntityState.Modified;
            return await Task.FromResult(entity);
        }
    }
}
