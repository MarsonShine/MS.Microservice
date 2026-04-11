using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Extension;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Infrastructure.Applications.Users;

namespace MS.Microservice.Web.Application.Users
{
    /// <summary>
    /// 创建用户的应用服务示例。
    /// 用它演示从 API -> AppService -> Domain -> Infrastructure 的函数式改写路径。
    /// </summary>
    public class UserCreateAppService(
        IUserDomainService userDomainService,
        CurrentUserResolver currentUserResolver) : IUserCreateAppService
    {
        private readonly IUserDomainService _userDomainService = userDomainService;
        private readonly CurrentUserResolver _currentUserResolver = currentUserResolver;

        /// <summary>
        /// 组合输入校验、角色校验、DTO 映射和领域调用。
        /// 其中可选值分支统一使用 Option 的 Match/Map 处理，避免散落的 null 判断。
        /// </summary>
        public async Task<Result<bool>> CreateAsync(UserCreatedCommand request, CancellationToken cancellationToken = default)
        {
            var currentUserResult = await _currentUserResolver.CurrentUserResultAsync(cancellationToken);
            return await currentUserResult.BindAsync(async currentUser =>
            {
                var validationResult = await request.ValidateResultAsync(cancellationToken);
                return await validationResult.BindAsync(async validRequest =>
                {
                    var rolesResult = await ResultExtensions.TryAsync(() => _userDomainService.GetAllRolesAsync(cancellationToken));
                    return await rolesResult.BindAsync(async roles =>
                    {
                        var userResult = validRequest
                            .EnsureRolesExistResult(roles)
                            .Bind(validCommand => validCommand.ToDomainUserResult(currentUser, _userDomainService.PasswordSalt()));

                        return await userResult.BindAsync(user => _userDomainService.CreateUserResultAsync(user, cancellationToken));
                    });
                });
            });
        }
    }
}
