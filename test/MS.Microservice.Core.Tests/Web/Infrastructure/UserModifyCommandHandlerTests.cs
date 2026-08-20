using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using MS.Microservice.Core.Identity;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Infrastructure.Caching.Consts;
using MS.Microservice.Web.Application.Commands;
using MS.Microservice.Web.Application.Identity;
using MS.Microservice.Web.Infrastructure.Applications.Users;
using NSubstitute;
using System.Security.Claims;
using System.Text;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public class UserModifyCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenPasswordIsProvided_PersistsVersionedHashAndClearsCache()
    {
        var userDomainService = Substitute.For<IUserDomainService>();
        var cache = Substitute.For<IDistributedCache>();
        var userPasswordService = Substitute.For<IUserPasswordService>();
        userPasswordService
            .HashPassword(Arg.Any<User>(), "Password123")
            .Returns("versioned-password-hash");
        userDomainService
            .FindFzAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(null));
        userDomainService
            .GetAllRolesAsync(Arg.Any<CancellationToken>())
            .Returns([new Role(1, "Admin", "Administrator")]);
        var existingUser = new PersistedUser(11);
        userDomainService
            .FindAsync("demo-account", Arg.Any<CancellationToken>())
            .Returns(existingUser);
        userDomainService
            .UpdateUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var currentUserResolver = CreateCurrentUserResolver(userDomainService);
        var handler = new UserModifyCommandHandler(
            userDomainService,
            cache,
            currentUserResolver,
            userPasswordService);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.Item1);
        Assert.Null(result.Item2);
        await userDomainService.Received(1).UpdateUserAsync(
            Arg.Is<User>(user =>
                user.Password == "versioned-password-hash"
                && user.Salt == User.ModernPasswordSaltMarker
                && user.HasModernPasswordHash()),
            Arg.Any<CancellationToken>());
        await cache.Received(1).RemoveAsync(
            CacheConsts.UserAccountKey + "demo-account",
            Arg.Any<CancellationToken>());
        await cache.Received(1).RemoveAsync(
            CacheConsts.UserIdKey + 11,
            Arg.Any<CancellationToken>());
    }

    private static CurrentUserResolver CreateCurrentUserResolver(IUserDomainService userDomainService)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtClaimTypes.Id, "7"),
                new Claim(JwtClaimTypes.NickName, "creator"),
                new Claim(JwtClaimTypes.PhoneNumber, "13800138000")
            ], "test"))
        };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return new CurrentUserResolver(accessor, userDomainService);
    }

    private static UserModifyCommand CreateCommand()
    {
        var encodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes("Password123"));
        return new UserModifyCommand(
            "demo-account",
            "Demo User",
            encodedPassword,
            "13800138000",
            "demo@example.com",
            [new RoleDto { Id = 1, Name = "Admin" }]);
    }

    private sealed class PersistedUser : User
    {
        public PersistedUser(int id)
            : base(
                "demo-account",
                "legacy-password-hash",
                "a1b2",
                false,
                "13800138000",
                1,
                1,
                "demo@example.com",
                "Demo User",
                string.Empty,
                string.Empty)
        {
            Id = id;
        }
    }
}
