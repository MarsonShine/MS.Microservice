using Microsoft.AspNetCore.Http;
using MS.Microservice.Core.Functional;
using MS.Microservice.Core.Identity;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Lab.Application.Commands;
using MS.Microservice.Lab.Application.Identity;
using MS.Microservice.Lab.Application.Users;
using MS.Microservice.Lab.Infrastructure.Applications.Users;
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
            userDomainService.CreateUserEitherAsync(Arg.Any<MS.Microservice.Domain.Aggregates.IdentityModel.User>(), Arg.Any<CancellationToken>())
                .Returns((Either<Error, bool>)F.Right(true));
            var userPasswordService = Substitute.For<IUserPasswordService>();
            userPasswordService
                .HashPassword(Arg.Any<User>(), "Password123")
                .Returns("versioned-password-hash");

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
            var service = new UserCreateAppService(userDomainService, currentUserResolver, userPasswordService);

            var result = await service.CreateAsync(CreateCommand([new RoleDto { Id = 1, Name = "Admin" }]));

            Assert.True(result.IsRight);
            Assert.True(result.Right);
            await userDomainService.Received(1).CreateUserEitherAsync(
                Arg.Is<MS.Microservice.Domain.Aggregates.IdentityModel.User>(user =>
                    user.Account == "demo-account"
                    && user.Password == "versioned-password-hash"
                    && user.Salt == User.ModernPasswordSaltMarker
                    && user.HasModernPasswordHash()
                    && user.CreatorId == 7
                    && user.Roles.Count == 1
                    && user.Roles.Single().Id == 1),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CreateAsync_WhenRoleIsMissing_ReturnsOriginalValidationAndDoesNotPersist()
        {
            var domain = Substitute.For<IUserDomainService>();
            domain.GetAllRolesAsync(Arg.Any<CancellationToken>()).Returns([new Role(1, "Admin", "管理员")]);
            var service = CreateService(domain);
            using var cancellation = new CancellationTokenSource();

            var result = await service.CreateAsync(CreateCommand([new RoleDto { Id = 9 }]), cancellation.Token);

            Assert.True(result.IsLeft);
            Assert.Equal("validation", result.Left.Code);
            Assert.Equal("角色校验失败", result.Left.Message);
            Assert.Equal(["传入的角色 Id 在系统中不存在。"], result.Left.Details);
            await domain.Received(1).GetAllRolesAsync(cancellation.Token);
            await domain.DidNotReceiveWithAnyArgs().CreateUserEitherAsync(default!, default);
        }

        [Fact]
        public async Task CreateAsync_WaitsForAsynchronousRoleLookupBeforePersisting()
        {
            var domain = Substitute.For<IUserDomainService>();
            var lookup = new TaskCompletionSource<List<Role>>(TaskCreationOptions.RunContinuationsAsynchronously);
            domain.GetAllRolesAsync(Arg.Any<CancellationToken>()).Returns(lookup.Task);
            domain.CreateUserEitherAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
                .Returns((Either<Error, bool>)F.Right(true));
            var password = Substitute.For<IUserPasswordService>();
            password.HashPassword(Arg.Any<User>(), "Password123").Returns("versioned-password-hash");
            var service = CreateService(domain, password);

            var pending = service.CreateAsync(CreateCommand([new RoleDto { Id = 1 }]));
            Assert.False(pending.IsCompleted);
            await domain.DidNotReceiveWithAnyArgs().CreateUserEitherAsync(default!, default);
            lookup.SetResult([new Role(1, "Admin", "管理员")]);

            var result = await pending;
            Assert.True(result.IsRight);
            await domain.Received(1).CreateUserEitherAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CreateAsync_WhenRoleCatalogFails_ReturnsExistingErrorAndDoesNotPersist()
        {
            var domain = Substitute.For<IUserDomainService>();
            domain.GetAllRolesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException<List<Role>>(new InvalidOperationException("catalog unavailable")));
            var service = CreateService(domain);

            var result = await service.CreateAsync(CreateCommand());

            Assert.True(result.IsLeft);
            Assert.Equal("user.roles", result.Left.Code);
            Assert.Equal("catalog unavailable", result.Left.Message);
            await domain.DidNotReceiveWithAnyArgs().CreateUserEitherAsync(default!, default);
        }

        [Fact]
        public async Task CreateAsync_WhenRoleCatalogContainsNull_PropagatesRuleFailure()
        {
            var domain = Substitute.For<IUserDomainService>();
            domain.GetAllRolesAsync(Arg.Any<CancellationToken>()).Returns([null!]);
            var service = CreateService(domain);

            await Assert.ThrowsAsync<NullReferenceException>(
                () => service.CreateAsync(CreateCommand([new RoleDto { Id = 1 }])));

            await domain.DidNotReceiveWithAnyArgs().CreateUserEitherAsync(default!, default);
        }

        [Fact]
        public async Task CreateAsync_WhenRoleLookupIsCanceled_PreservesExistingErrorAndToken()
        {
            using var cancellation = new CancellationTokenSource();
            var domain = Substitute.For<IUserDomainService>();
            domain.GetAllRolesAsync(Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.Equal(cancellation.Token, call.Arg<CancellationToken>());
                cancellation.Cancel();
                return Task.FromCanceled<List<Role>>(cancellation.Token);
            });
            var service = CreateService(domain);

            var result = await service.CreateAsync(CreateCommand(), cancellation.Token);

            Assert.True(result.IsLeft);
            Assert.Equal("user.roles", result.Left.Code);
            await domain.Received(1).GetAllRolesAsync(cancellation.Token);
            await domain.DidNotReceiveWithAnyArgs().CreateUserEitherAsync(default!, default);
        }

        [Fact]
        public async Task CreateAsync_WhenCompletedRoleLookupIgnoresCancellation_PreservesExistingFlow()
        {
            using var cancellation = new CancellationTokenSource();
            var domain = Substitute.For<IUserDomainService>();
            domain.GetAllRolesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromResult(new List<Role> { new(1, "Admin", "管理员") });
            });
            domain.CreateUserEitherAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
                .Returns((Either<Error, bool>)F.Right(true));
            var password = Substitute.For<IUserPasswordService>();
            password.HashPassword(Arg.Any<User>(), "Password123").Returns("versioned-password-hash");
            var service = CreateService(domain, password);

            var result = await service.CreateAsync(CreateCommand([new RoleDto { Id = 1 }]), cancellation.Token);

            Assert.True(result.IsRight);
            await domain.Received(1).CreateUserEitherAsync(Arg.Any<User>(), cancellation.Token);
        }

        [Fact]
        public async Task CreateAsync_WhenCommandIsInvalid_DoesNotQueryRoles()
        {
            var domain = Substitute.For<IUserDomainService>();
            var service = CreateService(domain);
            var command = new UserCreatedCommand("demo-account", "", Convert.ToBase64String(Encoding.UTF8.GetBytes("Password123")),
                "13800138000", "demo@example.com", []);

            var result = await service.CreateAsync(command);

            Assert.True(result.IsLeft);
            Assert.Equal("validation", result.Left.Code);
            await domain.DidNotReceiveWithAnyArgs().GetAllRolesAsync(default);
            await domain.DidNotReceiveWithAnyArgs().CreateUserEitherAsync(default!, default);
        }

        private static UserCreateAppService CreateService(IUserDomainService domain, IUserPasswordService? password = null)
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(JwtClaimTypes.Id, "7"),
                    new Claim(JwtClaimTypes.NickName, "creator"),
                    new Claim(JwtClaimTypes.PhoneNumber, "13800138000")
                ], "test"))
            };
            var accessor = Substitute.For<IHttpContextAccessor>();
            accessor.HttpContext.Returns(context);
            return new UserCreateAppService(domain, new CurrentUserResolver(accessor, domain),
                password ?? Substitute.For<IUserPasswordService>());
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
