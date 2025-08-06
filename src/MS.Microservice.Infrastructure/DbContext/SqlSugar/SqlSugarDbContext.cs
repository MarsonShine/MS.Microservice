using Microsoft.EntityFrameworkCore.Internal;
using MS.Microservice.Core.Domain.Entity.Enums;
using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Core.Domain.Repository.SqlSugar;
using MS.Microservice.Core.Dto;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.DbContext
{
	public abstract class SqlSugarDbContext<TEntity>(Func<ISqlSugarClient> clientFactory) : IRepositoryBase<TEntity>, ISqlSugarUnitOfWork
		where TEntity : class, new()
	{
        private readonly Lazy<ISqlSugarClient> _lazyDb = new(clientFactory);

        [NotNull]
		public ISqlSugarClient Db => _lazyDb.Value;//用来处理事务多表查询和复杂的操作

		/// <summary>
		/// 判断是否存在
		/// </summary>
		/// <param name="parm"></param>
		/// <param name="Async"></param>
		/// <returns></returns>
		public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> where)
		{
			return await Db.Queryable<TEntity>().AnyAsync(where);
		}

		#region 添加操作

		/// <summary>
		/// 添加
		/// </summary>
		/// <param name="parm"></param>
		/// <param name="Async"></param>
		/// <returns></returns>
		public async Task AddAsync(TEntity parm) => await Db.Insertable(parm).ExecuteCommandAsync();

		/// <summary>
		/// 添加返回最新id(仅支持id为int类型)
		/// </summary>
		/// <param name="parm"></param>
		/// <param name="Async"></param>
		/// <returns></returns>
		public async Task<int> AddAReturnIdsync(TEntity parm) => await Db.Insertable(parm).ExecuteReturnIdentityAsync();

		/// <summary>
		/// 添加返回最新实体
		/// </summary>
		/// <param name="parm"></param>
		/// <param name="Async"></param>
		/// <returns></returns>
		public async Task<TEntity> AddReturnEntitysync(TEntity parm) => await Db.Insertable(parm).ExecuteReturnEntityAsync();

		/// <summary>
		/// 批量添加数据
		/// </summary>
		/// <param name="parm">List<TEntity></param>
		/// <returns></returns>
		public async Task<bool> AddListAsync(List<TEntity> parm)
		{
			await Db.Insertable(parm).ExecuteCommandAsync();
			return true;
		}
		#endregion

		#region 查询操作
		/// <summary>
		/// 获得一条数据
		/// </summary>
		/// <param name="where">Expression<Func<TEntity, bool>></param>
		/// <returns></returns>
		public async Task<TEntity> GetModelAsync(Expression<Func<TEntity, bool>> where) => await Db.Queryable<TEntity>().FirstAsync(where);

		public async Task<PagedResultDto<TEntity>> GetPagesAsync(PagedRequestDto parm, Expression<Func<TEntity, bool>> where, Expression<Func<TEntity, object>> order, OrderByEnum orderEnum)
		{
			var query = Db.Queryable<TEntity>()
					.Where(where)
					.OrderByIF((int)orderEnum == 1, order, OrderByType.Asc)
					.OrderByIF((int)orderEnum == 2, order, OrderByType.Desc);
			var refCount = new RefAsync<int>();
			var list = await query.ToOffsetPageAsync(parm.PageIndex, parm.PageSize, refCount);
			return new PagedResultDto<TEntity>(refCount.Value, list);
		}

		public async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> where, Expression<Func<TEntity, object>> order, OrderByEnum orderEnum) => await Db.Queryable<TEntity>()
						.Where(where)
						.OrderByIF((int)orderEnum == 1, order, OrderByType.Asc)
						.OrderByIF((int)orderEnum == 2, order, OrderByType.Desc).ToListAsync();

		/// <summary>
		/// 获得列表
		/// </summary>
		/// <param name="parm">PageParm</param>
		/// <returns></returns>
		public async Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> where) => await Db.Queryable<TEntity>().Where(where).ToListAsync();

		/// <summary>
		/// 获得列表，不需要任何条件
		/// </summary>
		/// <returns></returns>
		public async Task<List<TEntity>> GetListAsync() => await Db.Queryable<TEntity>().ToListAsync();
		#endregion

		#region 修改操作
		/// <summary>
		/// 修改一条数据
		/// </summary>
		/// <param name="parm">T</param>
		/// <returns></returns>
		public async Task<bool> UpdateAsync(TEntity parm)
		{
			await Db.Updateable(parm).ExecuteCommandAsync();
			return true;
		}

		/// <summary>
		/// 批量修改
		/// </summary>
		/// <param name="parm">T</param>
		/// <returns></returns>
		public async Task<bool> UpdateAsync(List<TEntity> parm)
		{
			await Db.Updateable(parm).ExecuteCommandAsync();
			return true;
		}

		/// <summary>
		/// 修改一条数据，可用作假删除
		/// </summary>
		/// <param name="columns">修改的列=Expression<Func<TEntity,T>></param>
		/// <param name="where">Expression<Func<TEntity,bool>></param>
		/// <returns></returns>
		public async Task<bool> UpdateAsync(Expression<Func<TEntity, TEntity>> columns,
			Expression<Func<TEntity, bool>> where)
		{
			await Db.Updateable<TEntity>().SetColumns(columns).Where(where).ExecuteCommandAsync();
			return true;
		}
		#endregion

		#region 删除操作
		/// <summary>
		/// 删除多条数据
		/// </summary>
		/// <param name="parm">string</param>
		/// <returns></returns>
		public async Task<bool> DeleteAsync(List<int> parm)
		{
			await Db.Deleteable<TEntity>().In(parm.ToArray()).ExecuteCommandAsync();
			return true;
		}

		/// <summary>
		/// 删除一条或多条数据
		/// </summary>
		/// <param name="parm">string</param>
		/// <returns></returns>
		public async Task<bool> DeleteAsync(int id)
		{
			await Db.Deleteable<TEntity>(id).ExecuteCommandAsync();
			return true;
		}

		/// <summary>
		/// 删除一条或多条数据
		/// </summary>
		/// <param name="where">Expression<Func<TEntity, bool>></param>
		/// <returns></returns>
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
		#endregion
	}
}
