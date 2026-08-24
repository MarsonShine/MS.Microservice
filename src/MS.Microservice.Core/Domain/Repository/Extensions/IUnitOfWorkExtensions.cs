using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Functional;

namespace MS.Microservice.Core.Domain.Repository.Extensions;

public static partial class IUnitOfWorkExtensions
{
    extension(IUnitOfWork unitOfWork)
    {
        public Task<Either<Error, int>> SaveChangesEitherAsync(
            CancellationToken cancellationToken = default)
            => EitherExtensions.TryAsync(
                () => unitOfWork.SaveChangesAsync(cancellationToken),
                code: "persistence.save_changes");

        public async Task<Either<Error, bool>> SaveEntitiesEitherAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await EitherExtensions.TryAsync(
                () => unitOfWork.SaveEntitiesAsync(cancellationToken),
                code: "persistence.save_entities");
            return result.Where(
                predicate: saved => saved,
                leftFactory: _ => Error.Unexpected(
                    "持久化实体失败：SaveEntitiesAsync 返回 false。",
                    ["SaveEntitiesAsync returned false."]));
        }

        public Task<Result<int>> SaveChangesResultAsync(
            CancellationToken cancellationToken = default)
            => ResultExtensions.TryAsync(() => unitOfWork.SaveChangesAsync(cancellationToken));

        public async Task<Result<bool>> SaveEntitiesResultAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await ResultExtensions.TryAsync(
                () => unitOfWork.SaveEntitiesAsync(cancellationToken));
            return result.Ensure(
                saved => saved,
                () => new InvalidOperationException(
                    "持久化实体失败：SaveEntitiesAsync 返回 false。"));
        }
    }
}
