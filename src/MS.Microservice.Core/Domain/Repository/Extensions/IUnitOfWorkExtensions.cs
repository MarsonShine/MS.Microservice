using System;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Domain.Repository.Extensions
{
    public static class IUnitOfWorkExtensions
    {
        public static async Task<T> UnitOfWorkAsync<T>(this ISqlSugarUnitOfWork unitOfWork, Func<Task<T>> executeAsync)
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

        public static async Task UnitOfWorkAsync(this ISqlSugarUnitOfWork unitOfWork, Func<Task> executeAsync)
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
