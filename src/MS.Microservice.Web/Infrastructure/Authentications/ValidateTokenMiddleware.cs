using MS.Microservice.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Infrastructure.Authentications
{
    class ValidateTokenMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IdentityOptions _identityOptions;
        private readonly ILogger<ValidateTokenMiddleware> _logger;

        public ValidateTokenMiddleware(RequestDelegate next,
            IOptions<IdentityOptions> identityOptionsAccessor,
            ILogger<ValidateTokenMiddleware> logger)
        {
            if (identityOptionsAccessor == null || identityOptionsAccessor.Value == null)
            {
                throw new ArgumentNullException(nameof(IdentityOptions));
            }
            _next = next;
            _identityOptions = identityOptionsAccessor.Value;
            _logger = logger;
        }
        public async Task Invoke(HttpContext context)
        {
            var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

            if (token != null && !ValidateToken(context, token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    Code = StatusCodes.Status401Unauthorized,
                    Success = false,
                    Message = "Unauthorized"
                }));
            }
            await _next(context);
        }

        private bool ValidateToken(HttpContext context, string token)
        {
            try
            {
                var securityKeys = _identityOptions.JwtBearerOption.SecurityKeys
                        .Select(key => new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)));

                var tokenHandler = new JwtSecurityTokenHandler();
                
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidIssuers = _identityOptions.JwtBearerOption.Issuers,
                    ValidAudiences = _identityOptions.JwtBearerOption.Audiences,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = securityKeys,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                // 这里可以实现自动刷新 token
                var jwtToken = (JwtSecurityToken)validatedToken;
                //var userId = int.Parse(jwtToken.Claims.First(x => x.Type == "id").Value);

                var identity = new ClaimsIdentity(jwtToken.Claims);
                var principal = new ClaimsPrincipal(identity);
                context.User = principal;
                //// attach user to context on successful jwt validation
                //context.Items["User"] = userService.GetById(userId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return false;
                // do nothing if jwt validation fails
                // user is not attached to context so request won't have access to secure routes
            }
        }
    }
}
