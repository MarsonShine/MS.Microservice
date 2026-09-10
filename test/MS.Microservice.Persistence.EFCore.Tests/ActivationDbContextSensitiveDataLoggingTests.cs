using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using MS.Microservice.Domain;
using NSubstitute;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class ActivationDbContextSensitiveDataLoggingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Context_EnablesSensitiveDataLoggingOnlyWhenExplicitlyConfigured(bool enabled)
    {
        var dbContextOptions = new DbContextOptionsBuilder<ActivationDbContext>()
            .UseNpgsql("Host=localhost;Database=configuration-test;Username=test;Password=test")
            .Options;
        using var context = new ActivationDbContext(
            dbContextOptions,
            Options.Create(new MsPlatformDbContextSettings
            {
                EnableSensitiveDataLogging = enabled
            }),
            Substitute.For<IDomainEventDispatcher>());

        var coreOptions = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>();

        coreOptions.Should().NotBeNull();
        coreOptions!.IsSensitiveDataLoggingEnabled.Should().Be(enabled);
    }
}
