using IdentityModel;
using Microsoft.AspNetCore.Http;
using MS.Microservice.Core;
using MS.Microservice.Domain.Services.Interfaces;
using System;
using System.Linq;
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

        public async Task<CurrentUser?> CurrentUserAsync()
        {
            var claims = _httpContext.User.Claims;
            if (!claims.Any(p => p.Type == JwtClaimTypes.Id))
            {
                return null;
            }
            var id = claims.First(p => p.Type == JwtClaimTypes.Id).Value;
            var name = claims.First(p => p.Type == JwtClaimTypes.NickName).Value;
            var email = ""; //claims.First(p => p.Type == ClaimTypes.Email).Value;
            var phone = claims.First(p => p.Type == JwtClaimTypes.PhoneNumber).Value;
            int[] roles = new int[] { };
            //claims.First(p => p.Type == ClaimTypes.Role).Value.Split(';')
            //    ?.Select(r => int.Parse(r))
            //    ?.ToArray();

            var UserId = int.Parse(id);

            var user = await _userDomainService.FindFzAccountAsync(phone);
            if (user != null && user.IsTransient() == false)
            {
                UserId = user.Id;
            }

            return new CurrentUser(UserId, name, email, phone, roles);
        }
    }
}
