using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Core.Extension;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Aggregates.IdentityModel.Repository;
using MS.Microservice.Infrastructure.DbContext;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

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

        public override async Task<User> FindAsync([NotNull] Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Users
                .Include(u => u.Roles)
                    .ThenInclude(r => r.Actions)
                .Where(predicate)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<List<Role>> GetAllRoleAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Roles.AsNoTracking().ToListAsync(cancellationToken);
        }

        public async Task<User> GetAsync(int userId, CancellationToken cancellationToken = default)
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
