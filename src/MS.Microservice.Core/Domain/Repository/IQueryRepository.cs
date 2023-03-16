using MS.Microservice.Core.Specification;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Repository
{
    public interface IQueryRepository<T> where T : class
    {
        /// <summary>
        /// 根据主键Id获取实体
        /// </summary>
        /// <typeparam name="Tid"></typeparam>
        /// <param name="id"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<T> GetByIdAsync<Tid>(Tid id, CancellationToken cancellationToken = default) where Tid : notnull;
        /// <summary>
        /// 根据条件查询实体
        /// </summary>
        /// <typeparam name="Spec"></typeparam>
        /// <param name="specification"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<T> GetBySpecAsync<Spec>(Spec specification, CancellationToken cancellationToken = default) where Spec : ISingleResultSpecification, ISpecification<T>;
        /// <summary>
        /// 获取所有列表
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// 根据条件获取列表
        /// </summary>
        /// <param name="spec"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<List<T>> GetListAsync(ISpecification<T> spec, CancellationToken cancellationToken = default);
        /// <summary>
        /// 根据条件获取指定的实体信息
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="spec"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<List<TResult>> GetListAsync<TResult>(ISpecification<T, TResult> spec, CancellationToken cancellationToken = default);
    }
}
