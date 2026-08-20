using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using MS.Microservice.Core.Identity;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Infrastructure.Caching.Consts;
using MS.Microservice.Web.Application.Models.Caching;
using MS.Microservice.Web.Infrastructure.Authorizations.Requirements;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

/*
身份认证应由 JWT Bearer 中间件负责；RBAC 再次解析 Token 会产生两套可能不一致的认证规则。
授权操作不应该自动创建用户，否则一次权限检查会产生数据库写入。
使用“Token 角色与数据库当前角色的交集”可以让角色被撤销后及时失效，避免只相信旧 Token 中的角色。
默认拒绝保证任何缺失、非法或无法判断的状态都不会意外放行。

注意：这里特意删除授权过程中自动创建用户的数据库副作用。
 */
namespace MS.Microservice.Web.Infrastructure.Authorizations.Handlers
{
    public class RbacAuthorizationHandler : AuthorizationHandler<RbacRequirement>
    {
        private readonly IUserDomainService _userDomainService;
        private readonly IDistributedCache _cache;

        public RbacAuthorizationHandler(
            IUserDomainService userDomainService,
            IDistributedCache cache)
        {
            _userDomainService = userDomainService ?? throw new ArgumentNullException(nameof(userDomainService));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            RbacRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated != true
                || context.Resource is not HttpContext httpContext
                || !TryGetUserId(context.User, out var userId)
                || !TryGetClaimedRoleIds(context.User, requirement.ClaimType, out var claimedRoleIds)
                || !TryGetPermissionPath(httpContext, requirement, out var permissionPath))
            {
                context.Fail();
                return;
            }

            // 使用“Token 角色与数据库当前角色的交集”可以让角色被撤销后及时失效，避免只相信旧 Token 中的角色。
            var user = await FindUserAsync(userId, httpContext.RequestAborted);
            if (user is null || !HasPermission(user, claimedRoleIds, permissionPath))
            {
                context.Fail();
                return;
            }

            context.Succeed(requirement);
        }

        private async Task<UserCacheItem?> FindUserAsync(int userId, CancellationToken cancellationToken)
        {
            return await _cache.GetAsync(CacheConsts.UserIdKey + userId, async () =>
            {
                var user = await _userDomainService.GetUserAsync(userId, cancellationToken);
                return user is null || user.IsTransient() ? null : ToUserCache(user);
            }, cancellationToken: cancellationToken);
        }

        private static bool TryGetUserId(ClaimsPrincipal principal, out int userId)
        {
            var claimValue = principal.FindFirst(JwtClaimTypes.Id)?.Value;
            return int.TryParse(claimValue, out userId) && userId > 0;
        }

        private static bool TryGetClaimedRoleIds(
            ClaimsPrincipal principal,
            string claimType,
            [NotNullWhen(true)] out HashSet<int>? roleIds)
        {
            roleIds = [];
            var claims = principal.FindAll(claimType).ToArray();
            if (claims.Length == 0)
            {
                return false;
            }

            foreach (var value in claims.SelectMany(claim => claim.Value.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                if (!int.TryParse(value, out var roleId) || roleId <= 0)
                {
                    roleIds = null;
                    return false;
                }

                roleIds.Add(roleId);
            }

            if (roleIds.Count == 0)
            {
                roleIds = null;
                return false;
            }

            return true;
        }

        private static bool TryGetPermissionPath(
            HttpContext httpContext,
            RbacRequirement requirement,
            [NotNullWhen(true)] out string? permissionPath)
        {
            if (!string.IsNullOrWhiteSpace(requirement.Path))
            {
                permissionPath = NormalizePath(requirement.Path);
                return permissionPath.Length > 0;
            }

            var routeValues = httpContext.Request.RouteValues;
            var controller = routeValues["controller"]?.ToString();
            var action = routeValues["action"]?.ToString();
            if (string.IsNullOrWhiteSpace(controller) || string.IsNullOrWhiteSpace(action))
            {
                permissionPath = null;
                return false;
            }

            permissionPath = NormalizePath($"{controller}/{action}");
            return true;
        }

        private static bool HasPermission(
            UserCacheItem user,
            IReadOnlySet<int> claimedRoleIds,
            string permissionPath)
        {
            return user.Roles.Any(role =>
                claimedRoleIds.Contains(role.Id)
                && role.Actions?.Any(action =>
                    !string.IsNullOrWhiteSpace(action.Path)
                    && string.Equals(
                        NormalizePath(action.Path),
                        permissionPath,
                        StringComparison.OrdinalIgnoreCase)) == true);
        }

        private static string NormalizePath(string path) => path.Trim().Trim('/');

        private static UserCacheItem ToUserCache(User user) => new()
        {
            Account = user.Account,
            Email = user.Email,
            FzAccount = user.FzAccount,
            FzId = user.FzId,
            Id = user.Id,
            Name = user.Name,
            Password = user.Password,
            Roles = user.Roles.Select(role => new RoleCacheItem
            {
                Id = role.Id,
                Name = role.Name,
                Actions = role.Actions
                    .Select(action => new ActionCacheItem { Path = action.Path })
                    .ToList()
            }).ToList(),
            Salt = user.Salt,
            Telephone = user.Telephone,
        };
    }
}
