using MS.Microservice.Core.Domain.Repository.SqlSugar;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Functional;

namespace MS.Microservice.Core.Domain.Repository.Extensions;

public static class SqlSugarUnitOfWorkExtensions
{
    extension(ISqlSugarUnitOfWork unitOfWork)
    {
        public async Task<T> UnitOfWorkAsync<T>(Func<Task<T>> executeAsync)
        {
            try
            {
                await unitOfWork.BeginAsync();
                var result = await executeAsync();
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
            => unitOfWork.UnitOfWorkResultAsync(
                async () => await ResultExtensions.TryAsync(executeAsync));

        public Task<Either<Error, T>> UnitOfWorkEitherAsync<T>(Func<Task<T>> executeAsync)
            => unitOfWork.UnitOfWorkEitherAsync(
                async () => await EitherExtensions.TryAsync(
                    executeAsync,
                    code: "transaction.execute"));

        public async Task<Either<Error, T>> UnitOfWorkEitherAsync<T>(
            Func<Task<Either<Error, T>>> executeAsync)
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
            catch (Exception exception)
            {
                await TryRollbackAsync(unitOfWork);
                return F.Left(Error.FromException(exception, "transaction.execute"));
            }
        }

        public Task<Either<Error, Unit>> UnitOfWorkEitherAsync(Func<Task> executeAsync)
            => unitOfWork.UnitOfWorkEitherAsync(async () =>
            {
                await executeAsync();
                return Unit.Default;
            });

        public async Task<Result<T>> UnitOfWorkResultAsync<T>(
            Func<Task<Result<T>>> executeAsync)
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
            catch (Exception exception)
            {
                await TryRollbackAsync(unitOfWork);
                return Result<T>.Fail(exception);
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
            // A rollback failure must not replace the original business exception.
        }
    }
}
