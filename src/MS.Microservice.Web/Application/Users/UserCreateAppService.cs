using MS.Microservice.Core.Functional;
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
        public async Task<(bool Success, string? Message)> CreateAsync(UserCreatedCommand request, CancellationToken cancellationToken = default)
        {
            var currentUser = _currentUserResolver.CurrentUser() ?? throw new ArgumentException(nameof(CurrentUserResolver));
            var validationError = await request.ValidateAsync(cancellationToken);

            return await validationError.MatchAsync(
                none: async () =>
                {
                    var roles = await _userDomainService.GetAllRolesAsync(cancellationToken);
                    var maybeUser = request
                        .EnsureRolesExist(roles)
                        .Map(validRequest => validRequest.ToDomainUser(currentUser, _userDomainService.PasswordSalt()));

                    return await maybeUser.MatchAsync(
                        none: () => Task.FromResult<(bool Success, string? Message)>((false, "错误的角色参数")),
                        some: async user =>
                        {
                            var success = await _userDomainService.CreateUserAsync(user, cancellationToken);
                            return success
                                ? (true, (string?)null)
                                : (false, "用户创建失败");
                        });
                },
                some: message => Task.FromResult<(bool Success, string? Message)>((false, message)));
        }
    }
}
