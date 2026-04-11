using MS.Microservice.Core.Domain.Repository.SqlSugar;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Functional;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Domain.Repository.Extensions
{
    public static partial class IUnitOfWorkExtensions
    {
        extension(IUnitOfWork unitOfWork)
        {
            public Task<Result<int>> SaveChangesResultAsync(CancellationToken cancellationToken = default)
                => ResultExtensions.TryAsync(() => unitOfWork.SaveChangesAsync(cancellationToken));

            public async Task<Result<bool>> SaveEntitiesResultAsync(CancellationToken cancellationToken = default)
            {
                var result = await ResultExtensions.TryAsync(() => unitOfWork.SaveEntitiesAsync(cancellationToken));
                return result.Ensure(saved => saved, () => new InvalidOperationException("持久化实体失败。"));
            }
        }

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

            public Task<Result<T>> UnitOfWorkResultAsync<T>(Func<Task<T>> executeAsync)
                => unitOfWork.UnitOfWorkResultAsync(async () => await ResultExtensions.TryAsync(executeAsync));

            public async Task<Result<T>> UnitOfWorkResultAsync<T>(Func<Task<Result<T>>> executeAsync)
            {
                try
                {
                    await unitOfWork.BeginAsync();
                    var result = await executeAsync();

                    if (result.IsFailure)
                    {
                        await unitOfWork.RollbackAsync();
                        return result;
                    }

                    await unitOfWork.CommitAsync();
                    return result;
                }
                catch (Exception ex)
                {
                    await TryRollbackAsync(unitOfWork);
                    return Result<T>.Fail(ex);
                }
            }

            public Task<Result<Unit>> UnitOfWorkResultAsync(Func<Task> executeAsync)
                => unitOfWork.UnitOfWorkResultAsync(async () =>
                {
                    await executeAsync();
                    return Unit.Default;
                });
        }

        private static async Task TryRollbackAsync(ISqlSugarUnitOfWork unitOfWork)
        {
            try
            {
                await unitOfWork.RollbackAsync();
            }
            catch
            {
            }
        }
    }
}
