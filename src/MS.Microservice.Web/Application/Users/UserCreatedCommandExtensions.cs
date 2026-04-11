using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Application.Validations;
using MS.Microservice.Web.Infrastructure.Applications.Users;

namespace MS.Microservice.Web.Application.Users
{
    /// <summary>
    /// 基于 .NET 10 <c>extension</c> 语法，为创建用户命令补充函数式转换能力。
    /// 这些方法只负责纯计算：校验结果转化、角色合法性检查、DTO 到聚合的映射。
    /// </summary>
    public static class UserCreatedCommandExtensions
    {
        extension(UserCreatedCommand request)
        {
            /// <summary>
            /// 将 FluentValidation 的结果映射为“可选错误”。
            /// None 表示校验通过；Some(message) 表示存在校验错误。
            /// </summary>
            public async Task<Option<string>> ValidateAsync(CancellationToken cancellationToken = default)
            {
                var validator = new UserCreatedCommandValidator();
                var validationResult = await validator.ValidateAsync(request, cancellationToken);
                return validationResult.IsValid
                    ? F.None
                    : (Option<string>)F.Some(validationResult.ToString());
            }

            public async Task<Result<UserCreatedCommand>> ValidateResultAsync(CancellationToken cancellationToken = default)
            {
                var validator = new UserCreatedCommandValidator();
                var validationResult = await validator.ValidateAsync(request, cancellationToken);
                return validationResult.IsValid
                    ? Result<UserCreatedCommand>.Success(request)
                    : Result<UserCreatedCommand>.Fail(new ArgumentException(validationResult.ToString()));
            }

            /// <summary>
            /// 确认命令中的角色 Id 都存在于领域层给出的角色集合中。
            /// 成功时返回原命令，便于继续进入函数式管道；失败时返回 None。
            /// </summary>
            public Option<UserCreatedCommand> EnsureRolesExist(IReadOnlyCollection<Role> roles)
            {
                if (request.Roles.Count == 0)
                {
                    return F.Some(request);
                }

                var roleIds = roles.Select(role => role.Id).ToHashSet();
                return request.Roles.All(role => roleIds.Contains(role.Id))
                    ? F.Some(request)
                    : F.None;
            }

            public Result<UserCreatedCommand> EnsureRolesExistResult(IReadOnlyCollection<Role> roles)
                => request.EnsureRolesExist(roles).Match(
                    none: () => Result<UserCreatedCommand>.Fail(new ArgumentException("错误的角色参数")),
                    some: Result<UserCreatedCommand>.Success);

            /// <summary>
            /// 将 API 命令映射为领域聚合。
            /// 该转换保持纯函数特征，不做 IO，只做数据塑形。
            /// </summary>
            public User ToDomainUser(CurrentUser currentUser, string salt)
            {
                var user = new User(
                    request.Account,
                    request.Passowrd,
                    salt,
                    false,
                    request.Telephone,
                    currentUser.Id,
                    currentUser.Id,
                    request.Email,
                    request.UserName,
                    request.Account,
                    string.Empty);

                if (request.Roles.Count > 0)
                {
                    user.Roles.AddIfNotContains(
                        request.Roles.Select(role => new Role(role.Id, role.Name ?? string.Empty, string.Empty)));
                }

                return user;
            }

            public Result<User> ToDomainUserResult(CurrentUser currentUser, string salt)
                => ResultExtensions.Try(() => request.ToDomainUser(currentUser, salt));
        }
    }
}
