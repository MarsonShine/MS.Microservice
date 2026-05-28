using IdentityModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using MS.Microservice.Core.Functional;
using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Infrastructure.Caching.Consts;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Application.Users;
using MS.Microservice.Web.Infrastructure.Applications.Users;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional
{
    public class UserModifyAppServiceTests
    {
        [Fact]
        public async Task ModifyAsync_WhenPipelineIsValid_ReturnsSuccess()
        {
            var userDomainService = Substitute.For<IUserDomainService>();
            var cache = Substitute.For<IDistributedCache>();

            userDomainService.FindFzAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((User?)null);
            userDomainService.GetAllRolesAsync(Arg.Any<CancellationToken>())
                .Returns([new Role(1, "Admin", "管理员")]);

            var existingUser = new User(
                account: "demo-account",
                password: "old-password",
                salt: "salt-value",
                isDisabled: false,
                telephone: "13800138000",
                creatorId: 1,
                updatorId: 1,
                email: "demo@example.com",
                name: "原用户",
                fzAccount: string.Empty,
                fzId: string.Empty)
            {
                Id = 11
            };

            userDomainService.FindAsync("demo-account", Arg.Any<CancellationToken>())
                .Returns(existingUser);
            userDomainService.UpdateUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
                .Returns(true);

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtClaimTypes.Id, "7"),
                new Claim(JwtClaimTypes.NickName, "creator"),
                new Claim(JwtClaimTypes.PhoneNumber, "13800138000")
            ], "test"));

            var accessor = Substitute.For<IHttpContextAccessor>();
            accessor.HttpContext.Returns(httpContext);

            var currentUserResolver = new CurrentUserResolver(accessor, userDomainService);
            var service = new UserModifyAppService(userDomainService, currentUserResolver, cache);

            var result = await service.ModifyAsync(CreateCommand([new RoleDto { Id = 1, Name = "Admin" }]));

            Assert.True(result.IsRight);
            Assert.True(result.Right);
            await userDomainService.Received(1).UpdateUserAsync(
                Arg.Is<User>(user =>
                    user.Account == "demo-account"
                    && user.Salt == "salt-value"
                    && user.CreatorId == 7
                    && user.UpdatorId == 7
                    && user.Password == CryptologyHelper.HmacSha256("Password123" + "salt-value")
                    && user.Roles.Count == 1
                    && user.Roles.Single().Id == 1),
                Arg.Any<CancellationToken>());
            await cache.Received(1).RemoveAsync(CacheConsts.UserAccountKey + "demo-account", Arg.Any<CancellationToken>());
            await cache.Received(1).RemoveAsync(CacheConsts.UserIdKey + 11, Arg.Any<CancellationToken>());
        }

        private static UserModifyCommand CreateCommand(List<RoleDto>? roles = null)
        {
            var rawPassword = "Password123";
            var encodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawPassword));

            return new UserModifyCommand(
                account: "demo-account",
                userName: "示例用户",
                passowrd: encodedPassword,
                telephone: "13800138000",
                email: "demo@example.com",
                roles: roles ?? []);
        }
    }
}
