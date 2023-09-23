using MS.Microservice.Core.Domain.Entity.Enums;
using MS.Microservice.Core.Dto;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Domain.Repository
{
    public interface IRepositoryBase<T> where T : class
    {
        #region 插入操作
        Task<bool> AnyAsync(Expression<Func<T, bool>> where);
        Task AddAsync(T parm);

        /// <summary>
        /// 添加返回最新id(仅支持id为int类型)
        /// </summary>
        /// <param name="parm"></param>
        /// <param name="Async"></param>
        /// <returns></returns>
        Task<int> AddAReturnIdsync(T parm);

        /// <summary>
        /// 添加返回最新实体
        /// </summary>
        /// <param name="parm"></param>
        /// <param name="Async"></param>
        /// <returns></returns>
        Task<T> AddReturnEntitysync(T parm);

        /// <summary>
        /// 批量添加数据
        /// </summary>
        /// <param name="parm">List<T></param>
        /// <returns></returns>
        Task<bool> AddListAsync(List<T> parm);
        #endregion

        #region 查询操作
        /// <summary>
        /// 获得一条数据
        /// </summary>
        /// <param name="where">Expression<Func<T, bool>></param>
        /// <returns></returns>
        Task<T> GetModelAsync(Expression<Func<T, bool>> where);

        /// <summary>
        /// 分页
        /// </summary>
        /// <param name="parm">分页参数</param>
        /// <param name="where">条件</param>
        /// <param name="order">排序值</param>
        /// <param name="orderEnum">排序方式OrderByType</param>
        /// <returns></returns>
        Task<PagedResultDto<T>> GetPagesAsync(PagedRequestDto parm, Expression<Func<T, bool>> where,
            Expression<Func<T, object>> order, OrderByEnum orderEnum);

        /// <summary>
        /// 获得列表
        /// </summary>
        /// <param name="parm">PageParm</param>
        /// <returns></returns>
        Task<List<T>> GetListAsync(Expression<Func<T, bool>> where,
            Expression<Func<T, object>> order, OrderByEnum orderEnum);

        /// <summary>
        /// 获得列表
        /// </summary>
        /// <param name="parm">PageParm</param>
        /// <returns></returns>
        Task<List<T>> GetListAsync(Expression<Func<T, bool>> where);

        /// <summary>
        /// 获得列表，不需要任何条件
        /// </summary>
        /// <returns></returns>
        Task<List<T>> GetListAsync();
        #endregion

        #region 修改操作
        /// <summary>
        /// 修改一条数据
        /// </summary>
        /// <param name="parm">T</param>
        /// <returns></returns>
        Task<bool> UpdateAsync(T parm);

        /// <summary>
        /// 批量修改
        /// </summary>
        /// <param name="parm">T</param>
        /// <returns></returns>
        Task<bool> UpdateAsync(List<T> parm);

        /// <summary>
        /// 修改一条数据，可用作假删除
        /// </summary>
        /// <param name="columns">修改的列=Expression<Func<T,T>></param>
        /// <param name="where">Expression<Func<T,bool>></param>
        /// <returns></returns>
        Task<bool> UpdateAsync(Expression<Func<T, T>> columns, Expression<Func<T, bool>> where);
        #endregion

        #region 删除操作
        /// <summary>
        /// 删除多条数据
        /// </summary>
        /// <param name="parm">string</param>
        /// <returns></returns>
        Task<bool> DeleteAsync(List<int> parm);

        /// <summary>
        /// 删除一条或多条数据
        /// </summary>
        /// <param name="parm">string</param>
        /// <returns></returns>
        Task<bool> DeleteAsync(int id);

        /// <summary>
        /// 删除一条或多条数据
        /// </summary>
        /// <param name="where">Expression<Func<T, bool>></param>
        /// <returns></returns>
        Task<bool> DeleteAsync(Expression<Func<T, bool>> where);
        #endregion
    }
}
