using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Domain.Aggregates.LogAggregate;
using MS.Microservice.Domain.Aggregates.LogAggregate.Repository;
using MS.Microservice.Persistence.EFCore.DbContext;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Persistence.EFCore.Repository
{
    public class LogRepository : BasicRepositoryBase<LogAggregateRoot>, ILogRepository
    {
        private readonly ActivationDbContext _dbContext;

        public LogRepository(ActivationDbContext dbContext) : base(dbContext)
        {
            _dbContext = dbContext;
        }

        public override Task<bool> DeleteAsync([NotNull] Expression<Func<LogAggregateRoot, bool>> predicate, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override Task<bool> DeleteAsync([NotNull] LogAggregateRoot entity, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override Task<LogAggregateRoot?> FindAsync([NotNull] Expression<Func<LogAggregateRoot, bool>> predicate, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public override async Task<LogAggregateRoot> InsertAsync([NotNull] LogAggregateRoot entity, CancellationToken cancellationToken = default)
        {
            var user = await _dbContext.Logs.AddAsync(entity, cancellationToken);
            return user.Entity;
        }

        public override Task<LogAggregateRoot> UpdateAsync([NotNull] LogAggregateRoot entity, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
