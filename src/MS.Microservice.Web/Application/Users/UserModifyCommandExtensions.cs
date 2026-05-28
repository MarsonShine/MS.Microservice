using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Functional;
using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Consts;
using MS.Microservice.Infrastructure.Caching.Consts;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Application.Validations;
using MS.Microservice.Web.Infrastructure.Applications.Users;
using User = MS.Microservice.Domain.Aggregates.IdentityModel.User;

namespace MS.Microservice.Web.Application.Users
{
    public sealed record UserModifyState(
        UserModifyCommand Command,
        CurrentUser CurrentUser,
        IReadOnlyCollection<Role> AvailableRoles,
        User? ExistingUser);

    public sealed record UserModifyReadyState(
        UserModifyCommand Command,
        CurrentUser CurrentUser,
        IReadOnlyCollection<Role> AvailableRoles,
        User ExistingUser);

    public sealed record UserModifyExecution(User User, IReadOnlyList<string> CacheKeys);

    public static class UserModifyCommandExtensions
    {
        extension(UserModifyState state)
        {
            public async Task<Either<Error, UserModifyState>> ValidateAsync(CancellationToken cancellationToken = default)
            {
                var validator = new UserModifyCommandValidator();
                var validationResult = await validator.ValidateAsync(state.Command, cancellationToken);
                return validationResult.IsValid
                    ? F.Right(state)
                    : F.Left(Error.Validation("用户修改命令校验失败", validationResult.Errors.Select(error => error.ErrorMessage).ToArray()));
            }

            public Either<Error, UserModifyState> EnsureRolesExistEither()
            {
                if (state.Command.Roles.Count == 0)
                {
                    return F.Right(state);
                }

                var roleIds = state.AvailableRoles.Select(role => role.Id).ToHashSet();
                return state.Command.Roles.All(role => roleIds.Contains(role.Id))
                    ? F.Right(state)
                    : F.Left(Error.Validation("角色校验失败", ["传入的角色 Id 在系统中不存在。"]));
            }

            public Either<Error, UserModifyReadyState> EnsureExistingUserEither()
                => state.ExistingUser is null || state.ExistingUser.IsTransient()
                    ? F.Left(Error.Validation(ExceptionConsts.UserNotExisted, [$"Account={state.Command.Account}"]))
                    : F.Right(new UserModifyReadyState(state.Command, state.CurrentUser, state.AvailableRoles, state.ExistingUser));
        }

        extension(UserModifyReadyState state)
        {
            public Either<Error, UserModifyExecution> ToExecutionEither()
                => EitherExtensions.Try(() =>
                {
                    var salt = state.ExistingUser.Salt ?? string.Empty;
                    var password = state.Command.Passowrd.IsNotNullOrEmpty()
                        ? CryptologyHelper.HmacSha256(state.Command.Passowrd + salt)
                        : string.Empty;

                    var user = new User(
                        state.Command.Account,
                        password,
                        salt,
                        false,
                        state.Command.Telephone,
                        state.CurrentUser.Id,
                        state.CurrentUser.Id,
                        state.Command.Email,
                        state.Command.UserName,
                        string.Empty,
                        string.Empty);

                    if (state.Command.Roles.Count > 0)
                    {
                        user.Roles.AddIfNotContains(
                            state.Command.Roles.Select(role => new Role(role.Id, role.Name ?? string.Empty, string.Empty)));
                    }

                    var cacheKeys = new List<string>
                    {
                        CacheConsts.UserAccountKey + state.Command.Account,
                        CacheConsts.UserIdKey + state.ExistingUser.Id
                    };

                    if (state.ExistingUser.FzAccount.IsNotNullOrEmpty())
                    {
                        cacheKeys.Add(CacheConsts.UserFzAccountKey + state.ExistingUser.FzAccount);
                    }

                    return new UserModifyExecution(user, cacheKeys);
                }, code: "user.modify.mapping")
                .MapLeft(error => error with { Message = "用户修改命令映射为领域对象失败" });
        }
    }
}
