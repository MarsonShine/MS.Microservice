using Autofac;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Web.Application.Identity;
using MS.Microservice.Web.AutofacModules;
using NSubstitute;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public class UserPasswordServiceTests
{
    private const string Password = "Password123";

    [Fact]
    public void IdentityApplicationModule_ResolvesPasswordService()
    {
        var containerBuilder = new ContainerBuilder();
        containerBuilder.RegisterInstance(Substitute.For<IUserDomainService>());
        containerBuilder.RegisterModule<IdentityApplicationModule>();
        using var container = containerBuilder.Build();
        using var scope = container.BeginLifetimeScope();

        var service = scope.Resolve<IUserPasswordService>();

        Assert.IsType<UserPasswordService>(service);
    }

    [Fact]
    public async Task VerifyAndUpgradeAsync_WhenModernHashMatches_ReturnsTrueWithoutUpdate()
    {
        var passwordHasher = CreateHasher();
        var userDomainService = Substitute.For<IUserDomainService>();
        var user = CreateModernUser(passwordHasher);
        var service = new UserPasswordService(passwordHasher, userDomainService);

        var result = await service.VerifyAndUpgradeAsync(user, Password);

        Assert.True(result);
        await userDomainService.DidNotReceiveWithAnyArgs()
            .UpdatePasswordHashAsync(default!, default!, default);
    }

    [Fact]
    public async Task VerifyAndUpgradeAsync_WhenLegacyHashMatches_PersistsModernHash()
    {
        var passwordHasher = CreateHasher();
        var userDomainService = Substitute.For<IUserDomainService>();
        userDomainService
            .UpdatePasswordHashAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var user = CreateLegacyUser();
        var service = new UserPasswordService(passwordHasher, userDomainService);

        var result = await service.VerifyAndUpgradeAsync(user, Password);

        Assert.True(result);
        await userDomainService.Received(1).UpdatePasswordHashAsync(
            user,
            Arg.Is<string>(hash => passwordHasher.VerifyHashedPassword(user, hash, Password)
                == PasswordVerificationResult.Success),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAndUpgradeAsync_WhenPasswordIsWrong_DeniesWithoutUpdate()
    {
        var passwordHasher = CreateHasher();
        var userDomainService = Substitute.For<IUserDomainService>();
        var user = CreateLegacyUser();
        var service = new UserPasswordService(passwordHasher, userDomainService);

        var result = await service.VerifyAndUpgradeAsync(user, "WrongPassword123");

        Assert.False(result);
        await userDomainService.DidNotReceiveWithAnyArgs()
            .UpdatePasswordHashAsync(default!, default!, default);
    }

    [Fact]
    public async Task VerifyAndUpgradeAsync_WhenUpgradePersistenceFails_DeniesLogin()
    {
        var passwordHasher = CreateHasher();
        var userDomainService = Substitute.For<IUserDomainService>();
        userDomainService
            .UpdatePasswordHashAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var user = CreateLegacyUser();
        var service = new UserPasswordService(passwordHasher, userDomainService);

        var result = await service.VerifyAndUpgradeAsync(user, Password);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAndUpgradeAsync_WhenModernHashNeedsRehash_PersistsNewHash()
    {
        var oldHasher = CreateHasher(iterationCount: 10_000);
        var currentHasher = CreateHasher(iterationCount: 100_000);
        var userDomainService = Substitute.For<IUserDomainService>();
        userDomainService
            .UpdatePasswordHashAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var user = new PersistedUser(7, "placeholder", User.ModernPasswordSaltMarker);
        user.SetPasswordHash(oldHasher.HashPassword(user, Password));
        var service = new UserPasswordService(currentHasher, userDomainService);

        var result = await service.VerifyAndUpgradeAsync(user, Password);

        Assert.True(result);
        await userDomainService.Received(1).UpdatePasswordHashAsync(
            user,
            Arg.Is<string>(hash => currentHasher.VerifyHashedPassword(user, hash, Password)
                == PasswordVerificationResult.Success),
            Arg.Any<CancellationToken>());
    }

    private static PasswordHasher<User> CreateHasher(int iterationCount = 100_000)
        => new(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = iterationCount
        }));

    private static User CreateModernUser(IPasswordHasher<User> passwordHasher)
    {
        var user = new PersistedUser(7, "placeholder", User.ModernPasswordSaltMarker);
        user.SetPasswordHash(passwordHasher.HashPassword(user, Password));
        return user;
    }

    private static User CreateLegacyUser()
    {
        const string salt = "a1b2";
        return new PersistedUser(
            7,
            CryptologyHelper.HmacSha256(Password + salt),
            salt);
    }

    private sealed class PersistedUser : User
    {
        public PersistedUser(int id, string password, string salt)
            : base(
                "test-account",
                password,
                salt,
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
