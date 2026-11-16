using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
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
                    .BindAsync(validRequest => ValidateRolesAsync(validRequest, cancellationToken)
                        .Bind(validCommand => validCommand.ToDomainUserEither(currentUser))
                        .Map(user =>
                        {
                            user.SetPasswordHash(_userPasswordService.HashPassword(user, user.Password!));
                            return user;
                        })
                        .BindAsync(user => _userDomainService.CreateUserEitherAsync(user, cancellationToken))));

        private async Task<Either<Error, UserCreatedCommand>> ValidateRolesAsync(
            UserCreatedCommand request, CancellationToken cancellationToken)
        {
            var validation = new UserRoleCatalogValidation(request, _userDomainService);
            var valid = await Validator.TryValidateObjectAsync(validation, new ValidationContext(validation),
                validationResults: null, cancellationToken);
            if (validation.LookupError is { } error) return F.Left(error);
            return valid ? F.Right(request) : F.Left(UserRoleCatalogValidation.InvalidRoles());
        }
    }

    // This application-level rule performs the role lookup at the same point in the pipeline as before.
    internal sealed class UserRoleCatalogValidation(UserCreatedCommand request, IUserDomainService users)
        : IAsyncValidatableObject
    {
        private const string RoleErrorDetail = "传入的角色 Id 在系统中不存在。";

        internal static Error InvalidRoles() => Error.Validation("角色校验失败", [RoleErrorDetail]);

        internal Error? LookupError { get; private set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            => throw new InvalidOperationException("Role catalog validation requires asynchronous execution.");

        public async IAsyncEnumerable<ValidationResult> ValidateAsync(ValidationContext validationContext,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Match the original boundary: only catalog lookup failures are mapped to user.roles.
            var lookup = await EitherExtensions.TryAsync(() => users.GetAllRolesAsync(cancellationToken), "user.roles");
            if (lookup.IsLeft)
            {
                LookupError = lookup.Left;
                yield break;
            }
            if (request.EnsureRolesExist(lookup.Right).IsNone)
                yield return new ValidationResult(RoleErrorDetail, [nameof(UserCreatedCommand.Roles)]);
        }
    }
}
