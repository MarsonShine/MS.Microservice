using MS.Microservice.Core.Domain.Entity.Enums;
using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Core.Domain.Repository.SqlSugar;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Specification;
using MS.Microservice.Persistence.SqlSugar;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Persistence.SqlSugar.DbContext
{
    public abstract class BaseContext<TEntity> : IRepositoryBase<TEntity>, ISqlSugarUnitOfWork
        where TEntity : class, new()
    {
        public BaseContext(ISqlSugarClient sqlSugarClient)
        {
            Db = sqlSugarClient;
        }

        [NotNull]
        public ISqlSugarClient Db { get; }

        public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> where)
        {
            return await Db.Queryable<TEntity>().AnyAsync(where);
        }

        public async Task AddAsync(TEntity parm) => await Db.Insertable(parm).ExecuteCommandAsync();

        public async Task<int> AddAReturnIdsync(TEntity parm) => await Db.Insertable(parm).ExecuteReturnIdentityAsync();

        public async Task<TEntity> AddReturnEntitysync(TEntity parm) => await Db.Insertable(parm).ExecuteReturnEntityAsync();

        public async Task<bool> AddListAsync(List<TEntity> parm)
        {
            await Db.Insertable(parm).ExecuteCommandAsync();
            return true;
        }

        public async Task<TEntity> GetModelAsync(Expression<Func<TEntity, bool>> where) => await Db.Queryable<TEntity>().FirstAsync(where);

        public async Task<PagedResultDto<TEntity>> GetPagesAsync(PagedRequestDto parm, Expression<Func<TEntity, bool>> where,
            Expression<Func<TEntity, object>> order, OrderByEnum orderEnum)
        {
            var query = Db.Queryable<TEntity>()
                    .Where(where)
                    .OrderByIF((int)orderEnum == 1, order, OrderByType.Asc)
                    .OrderByIF((int)orderEnum == 2, order, OrderByType.Desc);
            var refCount = new RefAsync<int>();
            var list = await query.ToOffsetPageAsync(parm.PageIndex, parm.PageSize, refCount);
            return new PagedResultDto<TEntity>(refCount.Value, list);
        }

        public async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> where,
            Expression<Func<TEntity, object>> order, OrderByEnum orderEnum) => await Db.Queryable<TEntity>()
                        .Where(where)
                        .OrderByIF((int)orderEnum == 1, order, OrderByType.Asc)
                        .OrderByIF((int)orderEnum == 2, order, OrderByType.Desc).ToListAsync();

        public async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> where) => await Db.Queryable<TEntity>().Where(where).ToListAsync();

        public async Task<List<TEntity>> GetListAsync() => await Db.Queryable<TEntity>().ToListAsync();

        public async Task<bool> UpdateAsync(TEntity parm)
        {
            await Db.Updateable(parm).ExecuteCommandAsync();
            return true;
        }

        public async Task<bool> UpdateAsync(List<TEntity> parm)
        {
            await Db.Updateable(parm).ExecuteCommandAsync();
            return true;
        }

        public async Task<bool> UpdateAsync(Expression<Func<TEntity, TEntity>> columns,
            Expression<Func<TEntity, bool>> where)
        {
            await Db.Updateable<TEntity>().SetColumns(columns).Where(where).ExecuteCommandAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(List<int> parm)
        {
            await Db.Deleteable<TEntity>().In(parm.ToArray()).ExecuteCommandAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await Db.Deleteable<TEntity>(id).ExecuteCommandAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(Expression<Func<TEntity, bool>> where)
        {
            await Db.Deleteable<TEntity>().Where(where).ExecuteCommandAsync();
            return true;
        }

        public async Task BeginAsync()
        {
            await Db.Ado.BeginTranAsync();
        }

        public async Task CommitAsync()
        {
            await Db.Ado.CommitTranAsync();
        }

        public async Task RollbackAsync()
        {
            await Db.Ado.RollbackTranAsync();
        }

        public async Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> spec, CancellationToken ct = default)
        {
            return await Db.Queryable<TEntity>().ApplySpecification(spec).FirstAsync(ct);
        }

        public async Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<TEntity, TResult> spec, CancellationToken ct = default)
        {
            return await Db.Queryable<TEntity>().ApplySpecification(spec).FirstAsync(ct);
        }

        public async Task<List<TEntity>> ListAsync(ISpecification<TEntity> spec, CancellationToken ct = default) => await Db.Queryable<TEntity>().ApplySpecification(spec).ToListAsync(ct);

        public async Task<List<TResult>> ListAsync<TResult>(ISpecification<TEntity, TResult> spec, CancellationToken ct = default) => await Db.Queryable<TEntity>().ApplySpecification(spec).ToListAsync(ct);

        public async ValueTask<int> CountAsync(ISpecification<TEntity> spec, CancellationToken ct = default) => await Db.Queryable<TEntity>().ApplySpecification(spec, evaluateCriteriaOnly: true).CountAsync(ct);

        public async ValueTask<bool> AnyAsync(ISpecification<TEntity> spec, CancellationToken ct = default) => await Db.Queryable<TEntity>().ApplySpecification(spec, evaluateCriteriaOnly: true).AnyAsync(ct);
    }
}
