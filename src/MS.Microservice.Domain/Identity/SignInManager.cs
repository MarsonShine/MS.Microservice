using MS.Microservice.Core.Extension;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using IdentityModel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.Identity
{
    public class SignInManager
    {
        private HttpContext? _context;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly ILogger<SignInManager> _logger;
        private readonly IdentityOptions _identityOptions;
        public SignInManager(
            IHttpContextAccessor contextAccessor,
            ILogger<SignInManager> logger,
            IOptions<IdentityOptions> optionsAccessor
            )
        {
            if (optionsAccessor == null || optionsAccessor.Value == null)
            {
                throw new ArgumentNullException(nameof(optionsAccessor));
            }
            _contextAccessor = contextAccessor;
            _logger = logger;
            _identityOptions = optionsAccessor.Value;
        }

        public HttpContext Context
        {
            get
            {
                var context = (_context ?? _contextAccessor?.HttpContext) 
                    ?? throw new InvalidOperationException("HttpContext must not be null.");
                return context;
            }
            set
            {
                _context = value;
            }
        }

        public IdentityOptions IdentityOptions => _identityOptions;

        public virtual Task SignInAsync(User user, bool isPersistent, string? authenticationMethod = null)
            => SignInAsync(user, new AuthenticationProperties { IsPersistent = isPersistent }, authenticationMethod);

        public virtual Task SignInAsync(User user, AuthenticationProperties authenticationProperties, string? authenticationMethod = null)
        {
            IList<Claim> additionalClaims = Array.Empty<Claim>();
            if (authenticationMethod != null)
            {
                additionalClaims = new List<Claim>();
                additionalClaims.Add(new Claim(JwtClaimTypes.AuthenticationMethod, authenticationMethod));
            }
            return SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
        }

        public virtual async Task SignInWithClaimsAsync(User user, AuthenticationProperties authenticationProperties, IEnumerable<Claim> additionalClaims)
        {
            var userPrincipal = await CreateUserPrincipalAsync(user);
            foreach (var claim in additionalClaims)
            {
                userPrincipal.Identities.First().AddClaim(claim);
            }
            await Context.SignInAsync(IdentityConstants.ApplicationScheme,
                userPrincipal,
                authenticationProperties ?? new AuthenticationProperties());
        }

        public virtual async Task<ClaimsPrincipal> CreateUserPrincipalAsync(User? user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            var id = await GenerateClaimsAsync(user);
            return new ClaimsPrincipal(id);
        }

        protected virtual async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
        {
            var userId = user.Id.ToString();
            var userName = user.Name!;
            var id = new ClaimsIdentity(IdentityConstants.ApplicationScheme, JwtClaimTypes.NickName, JwtClaimTypes.Role);
            id.AddClaim(new Claim(JwtClaimTypes.Id, userId));
            id.AddClaim(new Claim(JwtClaimTypes.NickName, userName));
            id.AddClaim(new Claim(JwtClaimTypes.PhoneNumber, user.Telephone!));

            var roles = user.Roles
                .Select(p => p.Id + "_" + p.Name)
                .ToArray();
            id.AddClaim(new Claim(JwtClaimTypes.Role, roles.JoinAsString(";")));
            id.AddClaim(new Claim(JwtClaimTypes.Audience, IdentityOptions.AuthenticationOption!.Audiences.JoinAsString(",")));
            id.AddClaim(new Claim(JwtClaimTypes.Issuer, IdentityOptions.AuthenticationOption.Issuers.JoinAsString(",")));

            return await Task.FromResult(id);
        }
    }
}
