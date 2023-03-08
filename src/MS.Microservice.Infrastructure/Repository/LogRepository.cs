using MS.Microservice.Domain.Aggregates.LogAggregate.Repository;
using MS.Microservice.Infrastructure.DbContext;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using MS.Microservice.Core.Domain.Repository;
using System.Linq.Expressions;
using MS.Microservice.Domain.Aggregates.LogAggregate;

namespace MS.Microservice.Infrastructure.Repository
{
    public class LogRepository : BasicRepositoryBase<LogAggregateRoot>, ILogRepository
    {
        private readonly ActivationDbContext _dbContext;
        public LogRepository(ActivationDbContext dbContext):base(dbContext)
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

        public override Task<LogAggregateRoot> FindAsync([NotNull] Expression<Func<LogAggregateRoot, bool>> predicate, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        //插入日志
        public async override Task<LogAggregateRoot> InsertAsync([NotNull] LogAggregateRoot entity, CancellationToken cancellationToken = default)
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


