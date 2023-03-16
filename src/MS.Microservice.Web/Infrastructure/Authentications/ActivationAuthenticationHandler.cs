using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MS.Microservice.Core;
using MS.Microservice.Domain.Identity;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Infrastructure.Authentications
{
    public class ActivationAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IdentityOptions _identityOptions;
        public ActivationAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            IOptions<IdentityOptions> identityOptionsAccessor,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock) : base(options, logger, encoder, clock)
        {
            Check.NotNull(identityOptionsAccessor, nameof(IdentityOptions));
            Check.NotNull(identityOptionsAccessor.Value, nameof(IdentityOptions));

            _identityOptions = identityOptionsAccessor.Value;
        }

        protected override async Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

            var tokenHandler = new JwtSecurityTokenHandler();
            if (token != null)
            {
                try
                {
                    var securityKeys = _identityOptions.JwtBearerOption.SecurityKeys
                        .Select(key => new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)));

                    tokenHandler.ValidateToken(token, new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKeys = securityKeys,
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ClockSkew = TimeSpan.Zero
                    }, out SecurityToken validatedToken);

                    var identity = new ClaimsIdentity(Context.User.Claims);
                    var principal = new ClaimsPrincipal(identity);
                    var authenticationTicket = new AuthenticationTicket(principal, JwtBearerDefaults.AuthenticationScheme);
                    return Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(authenticationTicket);
                }

                catch (SecurityTokenExpiredException)
                {
                    return Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail("Unauthorized:Expired");
                }
                catch (SecurityTokenException)
                {
                    return Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail("Unauthorized");
                }
                catch (Exception)
                {
                    return Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail("Unauthorized");
                }
            }
            return await Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail("Unauthorized"));
        }
    }
}
