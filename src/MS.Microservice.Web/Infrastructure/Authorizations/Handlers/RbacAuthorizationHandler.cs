using MS.Microservice.Core.Extension;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Identity;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Infrastructure.Caching.Consts;
using MS.Microservice.Infrastructure.Common.Http.Extensions;
using MS.Microservice.Web.Application.Models.Caching;
using MS.Microservice.Web.Infrastructure.Authorizations.Requirements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Infrastructure.Authorizations.Handlers
{
    public class RbacAuthorizationHandler : AuthorizationHandler<RbacRequirement>
    {
        private readonly IdentityOptions _identityOptions;
        private readonly ILogger<RbacAuthorizationHandler> _logger;
        private readonly IUserDomainService _userDomainService;
        private readonly IDistributedCache _cache;
        public RbacAuthorizationHandler(
            IUserDomainService userDomainService,
            IOptions<IdentityOptions> identityOptionsAccessor,
            ILogger<RbacAuthorizationHandler> logger,
            IDistributedCache cache)
        {
            if (identityOptionsAccessor == null || identityOptionsAccessor.Value == null)
            {
                throw new ArgumentNullException(nameof(identityOptionsAccessor));
            }
            _identityOptions = identityOptionsAccessor.Value;
            _logger = logger;
            _userDomainService = userDomainService;
            _cache = cache;
        }
        public override async Task HandleAsync(AuthorizationHandlerContext context)
        {
            if (!context.User.Identity!.IsAuthenticated)
            {
                if (context.Resource is HttpContext httpContext)
                {
                    if (!httpContext.User.Identity!.IsAuthenticated)
                    {
                        //var endpoint = httpContext.GetEndpoint();
                        string? token = httpContext.Request.BearerAuthorization();
                        if (token.IsNotNullOrEmpty())
                        {
                            if (!await ValidateTokenAsync(httpContext, token))
                            {
                                _logger.LogInformation("token unthorization");
                                context.Fail();
                            }
                            else
                            {
                                context.User.AddIdentities(httpContext.User.Identities);
                                var identity = new ClaimsIdentity("BearerIdentity");
                                identity.AddClaims(httpContext.User.Claims);

                                var ju = UserClaimHelper.JWT2User(identity);
                                User? user = await _userDomainService.FindFzAccountAsync(ju.Account!);
                                if (user == null || user.IsTransient())
                                {
                                    var b = _userDomainService.CreateUserAsync(ju);
                                    if (!b.Result)
                                    {
                                        //创建失败
                                        _logger.LogError("自动创建用户失败:{ju}", ju);
                                    }
                                }
                                httpContext.Items["User"] = user;
                            }
                        }
                    }
                }
            }
            await base.HandleAsync(context);
        }

        private async ValueTask<bool> ValidateTokenAsync(HttpContext context, string token)
        {
            try
            {
                var securityKeys = _identityOptions.JwtBearerOption!.SecurityKeys!
                        .Select(key => new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)));

                var tokenHandler = new JsonWebTokenHandler();
                var result = await tokenHandler.ValidateTokenAsync(token, new TokenValidationParameters
                {
                    ValidIssuers = _identityOptions.JwtBearerOption.Issuers,
                    ValidAudiences = _identityOptions.JwtBearerOption.Audiences,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = securityKeys,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ClockSkew = TimeSpan.Zero
                });
                if (result.IsValid)
                {
                    var identity = new ClaimsIdentity("BearerIdentity");
                    var jwtToken = (JwtSecurityToken)result.SecurityToken;
                    identity.AddClaims(jwtToken.Claims);
                    var principal = new ClaimsPrincipal(identity);
                    context.User = principal;
                    return true;
                }
                // 这里可以实现自动刷新 token
                // attach user to context on successful jwt validation
                //context.Items["User"] = userService.GetById(userId);

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return false;
            }
        }

        //检查用户是否有Action 权限
        private async Task<bool> CheckActionAsync([NotNull] AuthorizationHandlerContext context)
        {
            if (context.Resource is HttpContext httpContext)
            {
                var identity = new ClaimsIdentity("BearerIdentity");
                identity.AddClaims(httpContext.User.Claims);

                var ju = UserClaimHelper.JWT2User(identity);


                //TODO:应用层 appservice
                var user = await FindFzAccountUserAsync(ju);
                if (user == null)
                {
                    user = await FindUserAsync(ju);
                }

                if (user == null && ju.FzAccount.IsNotNullOrEmpty())
                {
                    ju.AddRole(new Role(7, "ExternalStaff", "外部人员"));
                    var b = await _userDomainService.CreateUserAsync(ju);
                    if (!b)
                    {
                        //创建失败
                        _logger.LogError("自动创建用户失败:{ju}", ju);
                    }
                    else
                    {
                        user = ToUserCache(ju);
                    }

                }

                if (user == null)
                {
                    return false;
                }

                //判断是否有配合权限
                bool find = false;
                //临时调整需求放到2期实现
                find = true;
                //var route = httpContext.GetRouteData();
                //var controller = route.Values.First(r => r.Key == "controller");
                //var action = route.Values.First(r => r.Key == "action");

                //var questUrl = controller.Value + "/" + action.Value;
                //foreach (var role in user.Roles)
                //{
                //    foreach (var act in role.Actions)
                //    {
                //        if (string.Equals(act.Path, questUrl, StringComparison.OrdinalIgnoreCase))
                //        {
                //            find = true;
                //            return find;
                //        }
                //    }
                //}

                return find;
            }
            return false;
        }

        private async Task<UserCacheItem?> FindUserAsync(User ju)
        {
            var user = await _cache.GetAsync(CacheConsts.UserAccountKey + ju.Account, async () =>
            {
                var user = await _userDomainService.FindAsync(ju.Account!);
                if (user == null) return default;
                return ToUserCache(user);
            });
            return user;
        }

        private async Task<UserCacheItem?> FindFzAccountUserAsync(User ju)
        {
            UserCacheItem? user = await _cache.GetAsync(CacheConsts.UserFzAccountKey + ju.FzAccount, async () =>
            {
                var user = await _userDomainService.FindFzAccountAsync(ju.FzAccount!);
                if (user == null) return default;
                return ToUserCache(user);
            });
            return user;
        }

        private static UserCacheItem ToUserCache(User user) => new UserCacheItem
        {
            Account = user.Account,
            Email = user.Email,
            FzAccount = user.FzAccount,
            FzId = user.FzId,
            Id = user.Id,
            Name = user.Name,
            Password = user.Password,
            Roles = user.Roles.Select((r, i) => new RoleCacheItem
            {
                Id = r.Id,
                Name = r.Name,
                Actions = r.Actions.Select(ac => new ActionCacheItem { Path = ac.Path }).ToList()
            }).ToList(),
            Salt = user.Salt,
            Telephone = user.Telephone,
        };

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RbacRequirement requirement)
        {
            // 判断角色是否一致
            // c.Type == ClaimTypes.Role &&
            var rolesClaim = context.User.FindFirst(c => requirement.Issuers.Contains(c.Issuer));
            if (rolesClaim == null || rolesClaim.Value.IsNullOrEmpty())
            {
                context.Fail();
                await Task.CompletedTask;
                return;
            }


            if (await CheckActionAsync(context))
            {
                context.Succeed(requirement);
                return;
            }


            context.Fail();
            await Task.CompletedTask;
        }
    }
}
