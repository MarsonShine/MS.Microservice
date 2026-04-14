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
            public Task<Either<Error, int>> SaveChangesEitherAsync(CancellationToken cancellationToken = default)
                => EitherExtensions.TryAsync(() => unitOfWork.SaveChangesAsync(cancellationToken), code: "persistence.save_changes");

            public async Task<Either<Error, bool>> SaveEntitiesEitherAsync(CancellationToken cancellationToken = default)
            {
                var result = await EitherExtensions.TryAsync(() => unitOfWork.SaveEntitiesAsync(cancellationToken), code: "persistence.save_entities");
                return result.Where(
                    predicate: saved => saved,
                    leftFactory: _ => Error.Unexpected("持久化实体失败：SaveEntitiesAsync 返回 false。", ["SaveEntitiesAsync returned false."]));
            }

            public Task<Result<int>> SaveChangesResultAsync(CancellationToken cancellationToken = default)
                => ResultExtensions.TryAsync(() => unitOfWork.SaveChangesAsync(cancellationToken));

            public async Task<Result<bool>> SaveEntitiesResultAsync(CancellationToken cancellationToken = default)
            {
                var result = await ResultExtensions.TryAsync(() => unitOfWork.SaveEntitiesAsync(cancellationToken));
                return result.Ensure(saved => saved, () => new InvalidOperationException("持久化实体失败：SaveEntitiesAsync 返回 false。"));
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

            public Task<Either<Error, T>> UnitOfWorkEitherAsync<T>(Func<Task<T>> executeAsync)
                => unitOfWork.UnitOfWorkEitherAsync(async () => await EitherExtensions.TryAsync(executeAsync, code: "transaction.execute"));

            public async Task<Either<Error, T>> UnitOfWorkEitherAsync<T>(Func<Task<Either<Error, T>>> executeAsync)
            {
                try
                {
                    await unitOfWork.BeginAsync();
                    var result = await executeAsync();

                    if (result.IsLeft)
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
                    return F.Left(Error.FromException(ex, "transaction.execute"));
                }
            }

            public Task<Either<Error, Unit>> UnitOfWorkEitherAsync(Func<Task> executeAsync)
                => unitOfWork.UnitOfWorkEitherAsync(async () =>
                {
                    await executeAsync();
                    return Unit.Default;
                });

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
                // 回滚失败不应覆盖原始业务异常；这里故意吞掉二次异常。
            }
        }
    }
}
