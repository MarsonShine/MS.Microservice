using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using NSubstitute;

namespace MS.Microservice.Persistence.EFCore.Tests;

public sealed class LabSoftDeleteMappingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyExplicitUserRegistrationReceivesFilterAndIndex_WhenEnabled(bool enabled)
    {
        using var context = new ActivationDbContext(
            new DbContextOptionsBuilder<ActivationDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only;Username=test;Password=test")
                .EnableServiceProviderCaching(false).Options,
            Options.Create(new MsPlatformDbContextSettings { EnabledSoftDeleted = enabled }),
            Substitute.For<IDomainEventDispatcher>());
        var user = context.Model.FindEntityType(typeof(User))!;
        Assert.Equal(enabled, user.GetDeclaredQueryFilters().Any());
        Assert.Equal(enabled, user.GetIndexes().Any(index =>
            index.Properties.Count == 1 && index.Properties[0].Name == nameof(ISoftDeleted.DeletedAt)));
        Assert.Single(context.Model.FindEntityType(typeof(UserRole))!.GetDeclaredQueryFilters());
        Assert.All(context.Model.GetEntityTypes().Where(entity => entity.ClrType != typeof(User) && entity.ClrType != typeof(UserRole)),
            entity => Assert.Empty(entity.GetDeclaredQueryFilters()));
    }
}
