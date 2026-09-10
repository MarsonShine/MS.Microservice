using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Lab.Application.Commands;
using MS.Microservice.Lab.Application.Identity;
using MS.Microservice.Lab.Infrastructure.Applications.Users;

namespace MS.Microservice.Lab.Application.Users
{
    /// <summary>
    /// 创建用户的应用服务示例。
    /// 用它演示从 API -> AppService -> Domain -> Infrastructure 的函数式改写路径。
    /// </summary>
    public class UserCreateAppService(
        IUserDomainService userDomainService,
        CurrentUserResolver currentUserResolver,
        IUserPasswordService userPasswordService) : IUserCreateAppService
    {
        private readonly IUserDomainService _userDomainService = userDomainService;
        private readonly CurrentUserResolver _currentUserResolver = currentUserResolver;
        private readonly IUserPasswordService _userPasswordService = userPasswordService;

        /// <summary>
        /// 组合输入校验、角色校验、DTO 映射和领域调用。
        /// 其中可选值分支统一使用 Option 的 Match/Map 处理，避免散落的 null 判断。
        /// </summary>
        public async Task<Either<Error, bool>> CreateAsync(UserCreatedCommand request, CancellationToken cancellationToken = default)
            => await _currentUserResolver.CurrentUserEitherAsync(cancellationToken)
                .BindAsync(currentUser => request.ValidateEitherAsync(cancellationToken)
                    .BindAsync(validRequest => EitherExtensions.TryAsync(() => _userDomainService.GetAllRolesAsync(cancellationToken), code: "user.roles")
                        .Bind(roles => validRequest.EnsureRolesExistEither(roles))
                        .Bind(validCommand => validCommand.ToDomainUserEither(currentUser))
                        .Map(user =>
                        {
                            user.SetPasswordHash(_userPasswordService.HashPassword(user, user.Password!));
                            return user;
                        })
                        .BindAsync(user => _userDomainService.CreateUserEitherAsync(user, cancellationToken))));
    }
}
