using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.Aggregates.LogAggregate.Repository
{
    public interface ILogRepository
    {
        /// <summary>
        /// 添加记录日志
        /// <para>用于追踪员工操作记录的日志</para>
        /// </summary>
        /// <param name="activateCode"></param>
        /// <returns></returns>
        Task<LogAggregateRoot> InsertAsync(LogAggregateRoot log, CancellationToken cancellationToken = default);

    }
}
