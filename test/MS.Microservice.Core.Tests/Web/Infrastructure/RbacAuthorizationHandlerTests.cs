using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using MS.Microservice.Core.Identity;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Web.Application.Models.Caching;
using MS.Microservice.Web.Infrastructure.Authorizations.Handlers;
using MS.Microservice.Web.Infrastructure.Authorizations.Requirements;
using NSubstitute;
using System.Security.Claims;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public class RbacAuthorizationHandlerTests
{
    [Fact]
    public void UserCacheItem_DoesNotExposePasswordOrSalt()
    {
        Assert.Null(typeof(UserCacheItem).GetProperty("Password"));
        Assert.Null(typeof(UserCacheItem).GetProperty("Salt"));
    }

    [Fact]
    public async Task HandleAsync_WhenPrincipalIsAnonymous_DeniesWithoutQueryingUser()
    {
        var (handler, userDomainService) = CreateHandler();
        var context = CreateContext(authenticated: false);

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        await userDomainService.DidNotReceiveWithAnyArgs().GetUserAsync(default, default);
    }

    [Theory]
    [InlineData(null, "1")]
    [InlineData("invalid-user-id", "1")]
    [InlineData("7", null)]
    [InlineData("7", "invalid-role-id")]
    [InlineData("7", "1;invalid-role-id")]
    public async Task HandleAsync_WhenRequiredClaimsAreMissingOrInvalid_Denies(
        string? userIdClaim,
        string? roleClaim)
    {
        var (handler, userDomainService) = CreateHandler();
        var context = CreateContext(userIdClaim: userIdClaim, roleClaim: roleClaim);

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        await userDomainService.DidNotReceiveWithAnyArgs().GetUserAsync(default, default);
    }

    [Fact]
    public async Task HandleAsync_WhenRouteCannotBeResolved_DeniesWithoutQueryingUser()
    {
        var (handler, userDomainService) = CreateHandler();
        var context = CreateContext(controller: null, action: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        await userDomainService.DidNotReceiveWithAnyArgs().GetUserAsync(default, default);
    }

    [Fact]
    public async Task HandleAsync_WhenUserDoesNotExist_DeniesWithoutCreatingUser()
    {
        var (handler, userDomainService) = CreateHandler();
        userDomainService
            .GetUserAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(null));
        var context = CreateContext();

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        await userDomainService.DidNotReceive()
            .CreateUserAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenClaimedRoleIsNotAssignedToUser_Denies()
    {
        var (handler, userDomainService) = CreateHandler();
        var user = CreateUser(7, roleId: 1, "User/List");
        userDomainService
            .GetUserAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        var context = CreateContext(roleClaim: "2");

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task HandleAsync_WhenRoleDoesNotContainRoutePermission_Denies()
    {
        var (handler, userDomainService) = CreateHandler();
        var user = CreateUser(7, roleId: 1, "User/Modify");
        userDomainService
            .GetUserAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        var context = CreateContext();

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task HandleAsync_WhenClaimedRoleContainsRoutePermission_SucceedsCaseInsensitively()
    {
        var (handler, userDomainService) = CreateHandler();
        var user = CreateUser(7, roleId: 1, "USER/LIST");
        userDomainService
            .GetUserAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<User?>(user));
        var context = CreateContext(controller: "user", action: "list");

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    private static (RbacAuthorizationHandler Handler, IUserDomainService UserDomainService) CreateHandler()
    {
        var userDomainService = Substitute.For<IUserDomainService>();
        var cache = Substitute.For<IDistributedCache>();
        cache
            .GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));
        cache
            .SetAsync(
                Arg.Any<string>(),
                Arg.Any<byte[]>(),
                Arg.Any<DistributedCacheEntryOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        return (new RbacAuthorizationHandler(userDomainService, cache), userDomainService);
    }

    private static AuthorizationHandlerContext CreateContext(
        bool authenticated = true,
        string? userIdClaim = "7",
        string? roleClaim = "1",
        string? controller = "User",
        string? action = "List")
    {
        var requirement = new RbacRequirement(["test-issuer"], JwtClaimTypes.Role, string.Empty);
        var claims = new List<Claim>();
        if (userIdClaim is not null)
        {
            claims.Add(new Claim(JwtClaimTypes.Id, userIdClaim));
        }

        if (roleClaim is not null)
        {
            claims.Add(new Claim(JwtClaimTypes.Role, roleClaim));
        }

        var identity = new ClaimsIdentity(
            claims,
            authenticated ? "test-authentication" : null,
            JwtClaimTypes.NickName,
            JwtClaimTypes.Role);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues = new RouteValueDictionary();
        if (controller is not null)
        {
            httpContext.Request.RouteValues["controller"] = controller;
        }

        if (action is not null)
        {
            httpContext.Request.RouteValues["action"] = action;
        }

        return new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(identity),
            httpContext);
    }

    private static User CreateUser(int userId, int roleId, params string[] permissionPaths)
    {
        var user = new TestUser(userId);
        var role = new Role(roleId, $"role-{roleId}", "test role");
        foreach (var permissionPath in permissionPaths)
        {
            role.AddAction(permissionPath, permissionPath);
        }

        user.AddRole(role);
        return user;
    }

    private sealed class TestUser : User
    {
        public TestUser(int id)
            : base(
                "test-account",
                "test-password",
                "test-salt",
                false,
                "13800138000",
                1,
                1,
                "test@example.com",
                "Test User",
                "test-fz-account",
                "test-fz-id")
        {
            Id = id;
        }
    }
}
