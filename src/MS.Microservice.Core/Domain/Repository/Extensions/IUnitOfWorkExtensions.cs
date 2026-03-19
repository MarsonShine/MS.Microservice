using System;
using System.Threading.Tasks;
using MS.Microservice.Core.Domain.Repository.SqlSugar;

namespace MS.Microservice.Core.Domain.Repository.Extensions
{
    public static partial class IUnitOfWorkExtensions
    {
        extension(ISqlSugarUnitOfWork unitOfWork)
        {
            public async Task<T> UnitOfWorkAsync<T>(Func<Task<T>> executeAsync)
            {
                try
                {
                    await unitOfWork.BeginAsync();
                    T result = await executeAsync();
                    await unitOfWork.CommitAsync();
                    return result;
                }
                catch
                {
                    await unitOfWork.RollbackAsync();
                    throw;
                }
            }

            public async Task UnitOfWorkAsync(Func<Task> executeAsync)
            {
                try
                {
                    await unitOfWork.BeginAsync();
                    await executeAsync();
                    await unitOfWork.CommitAsync();
                }
                catch
                {
                    await unitOfWork.RollbackAsync();
                    throw;
                }
            }
        }
    }
}
