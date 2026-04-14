using IdentityModel;
using Microsoft.AspNetCore.Http;
using MS.Microservice.Core;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Services.Interfaces;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Infrastructure.Applications.Users
{
    public class CurrentUserResolver
    {
        private readonly HttpContext _httpContext;
        private readonly IUserDomainService _userDomainService;
        public CurrentUserResolver(IHttpContextAccessor httpContextAccessor, IUserDomainService userDomainService)
        {
            Check.NotNull(httpContextAccessor, nameof(httpContextAccessor));
            if (httpContextAccessor.HttpContext == null)
                throw new ArgumentException(nameof(HttpContext));

            _httpContext = httpContextAccessor.HttpContext;
            _userDomainService = userDomainService;
        }

        public CurrentUser? CurrentUser()
        {
            return CurrentUserAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public async Task<CurrentUser?> CurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var result = await CurrentUserEitherAsync(cancellationToken);
            return result.Match(
                left: _ => (CurrentUser?)null,
                right: user => user);
        }

        public Task<Either<Error, CurrentUser>> CurrentUserEitherAsync(CancellationToken cancellationToken = default)
        {
            return EitherExtensions.TryAsync(async () =>
            {
                var claims = _httpContext.User.Claims;
                if (!claims.Any(p => p.Type == JwtClaimTypes.Id))
                {
                    throw new InvalidOperationException("当前请求缺少用户身份。");
                }

                var id = claims.First(p => p.Type == JwtClaimTypes.Id).Value;
                var name = claims.First(p => p.Type == JwtClaimTypes.NickName).Value;
                var email = "";
                var phone = claims.First(p => p.Type == JwtClaimTypes.PhoneNumber).Value;
                int[] roles = [];

                var userId = int.Parse(id);
                var user = await _userDomainService.FindFzAccountAsync(phone, cancellationToken);
                if (user != null && user.IsTransient() == false)
                {
                    userId = user.Id;
                }

                return new CurrentUser(userId, name, email, phone, roles);
            }, code: "user.current");
        }

        public async Task<Result<CurrentUser>> CurrentUserResultAsync(CancellationToken cancellationToken = default)
        {
            var either = await CurrentUserEitherAsync(cancellationToken);
            return either.Match(
                left: error => Result<CurrentUser>.Fail(new InvalidOperationException(error.ToDisplayMessage())),
                right: Result<CurrentUser>.Success);
        }
    }
}
