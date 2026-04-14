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
        {
            var currentUserResult = await _currentUserResolver.CurrentUserEitherAsync(cancellationToken);
            if (currentUserResult.IsLeft)
            {
                return F.Left(currentUserResult.Left);
            }

            var executionResult = await BuildExecutionAsync(request, currentUserResult.Right, cancellationToken);
            if (executionResult.IsLeft)
            {
                return F.Left(executionResult.Left);
            }

            return await PersistAsync(executionResult.Right, cancellationToken);
        }

        private async Task<Either<Error, UserModifyExecution>> BuildExecutionAsync(
            UserModifyCommand request,
            CurrentUser currentUser,
            CancellationToken cancellationToken)
        {
            var initialState = new UserModifyState(request, currentUser, [], null);
            var validatedState = await initialState.ValidateAsync(cancellationToken);
            if (validatedState.IsLeft)
            {
                return F.Left(validatedState.Left);
            }

            var rolesResult = await EitherExtensions.TryAsync(() => _userDomainService.GetAllRolesAsync(cancellationToken), code: "user.roles");
            if (rolesResult.IsLeft)
            {
                return F.Left(rolesResult.Left);
            }

            var existingUserResult = await EitherExtensions.TryAsync(() => _userDomainService.FindAsync(validatedState.Right.Command.Account, cancellationToken), code: "user.modify.load");
            if (existingUserResult.IsLeft)
            {
                return F.Left(existingUserResult.Left);
            }

            return ((Either<Error, UserModifyState>)F.Right(validatedState.Right with
            {
                AvailableRoles = rolesResult.Right,
                ExistingUser = existingUserResult.Right
            }))
                .Bind(state => state.EnsureRolesExistEither())
                .Bind(state => state.EnsureExistingUserEither())
                .Bind(state => state.ToExecutionEither());
        }

        private async Task<Either<Error, bool>> PersistAsync(UserModifyExecution execution, CancellationToken cancellationToken)
        {
            var updateResult = await EitherExtensions.TryAsync(() => _userDomainService.UpdateUserAsync(execution.User, cancellationToken), code: "user.modify.update");
            var ensuredUpdateResult = updateResult.Where(
                predicate: updated => updated,
                leftFactory: _ => Error.Unexpected("用户修改失败", ["UpdateUserAsync returned false."]));

            if (ensuredUpdateResult.IsLeft)
            {
                return F.Left(ensuredUpdateResult.Left);
            }

            var clearCacheResult = await ClearCacheAsync(execution.CacheKeys, cancellationToken);
            return clearCacheResult.Match<Either<Error, bool>>(
                left: error => F.Left(error),
                right: _ => F.Right(true));
        }

        private async Task<Either<Error, Unit>> ClearCacheAsync(IReadOnlyList<string> cacheKeys, CancellationToken cancellationToken)
        {
            var result = (Either<Error, Unit>)F.Right(Unit.Default);

            foreach (var cacheKey in cacheKeys.Distinct())
            {
                var currentKey = cacheKey;
                if (result.IsLeft)
                {
                    return result;
                }

                var removeResult = await EitherExtensions.TryAsync(() => _cache.RemoveAsync(currentKey, cancellationToken), code: "user.cache.remove");
                result = removeResult.MapLeft(error => error with { Message = $"清理用户缓存失败: {currentKey}" });
            }

            return result;
        }
    }
}
