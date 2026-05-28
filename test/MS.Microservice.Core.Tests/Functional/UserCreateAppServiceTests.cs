using IdentityModel;
using Microsoft.AspNetCore.Http;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Services.Interfaces;
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
    public class UserCreateAppServiceTests
    {
        [Fact]
        public async Task CreateAsync_WhenPipelineIsValid_ReturnsSuccess()
        {
            var userDomainService = Substitute.For<IUserDomainService>();
            userDomainService.FindFzAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((MS.Microservice.Domain.Aggregates.IdentityModel.User?)null);
            userDomainService.GetAllRolesAsync(Arg.Any<CancellationToken>())
                .Returns([new Role(1, "Admin", "管理员")]);
            userDomainService.PasswordSalt()
                .Returns("salt-value");
            userDomainService.CreateUserEitherAsync(Arg.Any<MS.Microservice.Domain.Aggregates.IdentityModel.User>(), Arg.Any<CancellationToken>())
                .Returns((Either<Error, bool>)F.Right(true));

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
            var service = new UserCreateAppService(userDomainService, currentUserResolver);

            var result = await service.CreateAsync(CreateCommand([new RoleDto { Id = 1, Name = "Admin" }]));

            Assert.True(result.IsRight);
            Assert.True(result.Right);
            await userDomainService.Received(1).CreateUserEitherAsync(
                Arg.Is<MS.Microservice.Domain.Aggregates.IdentityModel.User>(user =>
                    user.Account == "demo-account"
                    && user.Salt == "salt-value"
                    && user.CreatorId == 7
                    && user.Roles.Count == 1
                    && user.Roles.Single().Id == 1),
                Arg.Any<CancellationToken>());
        }

        private static UserCreatedCommand CreateCommand(List<RoleDto>? roles = null)
        {
            var rawPassword = "Password123";
            var encodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawPassword));

            return new UserCreatedCommand(
                account: "demo-account",
                userName: "示例用户",
                passowrd: encodedPassword,
                telephone: "13800138000",
                email: "demo@example.com",
                roles: roles ?? []);
        }
    }
}
