using Microsoft.Extensions.Caching.Distributed;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Infrastructure.Applications.Users;

namespace MS.Microservice.Web.Application.Users
{
    public class UserModifyAppService(
        IUserDomainService userDomainService,
        CurrentUserResolver currentUserResolver,
        IDistributedCache cache) : IUserModifyAppService
    {
        private readonly IUserDomainService _userDomainService = userDomainService;
        private readonly CurrentUserResolver _currentUserResolver = currentUserResolver;
        private readonly IDistributedCache _cache = cache;

        public async Task<Either<Error, bool>> ModifyAsync(UserModifyCommand request, CancellationToken cancellationToken = default)
            => await _currentUserResolver.CurrentUserEitherAsync(cancellationToken)
                .BindAsync(currentUser => BuildExecutionAsync(request, currentUser, cancellationToken))
                .BindAsync(execution => PersistAsync(execution, cancellationToken));

        private async Task<Either<Error, UserModifyExecution>> BuildExecutionAsync(
            UserModifyCommand request,
            CurrentUser currentUser,
            CancellationToken cancellationToken)
            => await EitherExtensions.RightAsync<Error, UserModifyState>(new UserModifyState(request, currentUser, [], null))
                .BindAsync(state => state.ValidateAsync(cancellationToken))
                .BindAsync(state => EitherExtensions.TryAsync(() => _userDomainService.GetAllRolesAsync(cancellationToken), code: "user.roles")
                    .Map(roles => state with { AvailableRoles = roles }))
                .BindAsync(state => EitherExtensions.TryAsync(() => _userDomainService.FindAsync(state.Command.Account, cancellationToken), code: "user.modify.load")
                    .Map(existingUser => state with { ExistingUser = existingUser }))
                .Bind(state => state.EnsureRolesExistEither())
                .Bind(state => state.EnsureExistingUserEither())
                .Bind(state => state.ToExecutionEither());

        private async Task<Either<Error, bool>> PersistAsync(UserModifyExecution execution, CancellationToken cancellationToken)
            => await EitherExtensions.TryAsync(() => _userDomainService.UpdateUserAsync(execution.User, cancellationToken), code: "user.modify.update")
                .Where(
                    predicate: updated => updated,
                    leftFactory: _ => Error.Unexpected("用户修改失败", ["UpdateUserAsync returned false."]))
                .BindAsync(_ => ClearCacheAsync(execution.CacheKeys, cancellationToken)
                    .Map(_ => true));

        private async Task<Either<Error, Unit>> ClearCacheAsync(IReadOnlyList<string> cacheKeys, CancellationToken cancellationToken)
            => await cacheKeys
                .Distinct()
                .Aggregate(
                    EitherExtensions.RightAsync<Error, Unit>(Unit.Default),
                    (pipeline, cacheKey) => pipeline.BindAsync(_ => EitherExtensions.TryAsync(() => _cache.RemoveAsync(cacheKey, cancellationToken), code: "user.cache.remove")
                        .MapLeft(error => error with { Message = $"清理用户缓存失败: {cacheKey}" })));
    }
}
