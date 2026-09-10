using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Domain;
using MS.Microservice.Reference.Persistence;

namespace MS.Microservice.Messaging.IntegrationTests;

[Collection(MessagingRecoveryCollection.Name)]
public sealed class MessagingContractTests(MessagingRecoveryFixture fixture)
{
    [MessagingIntegrationTheory]
    [InlineData("SelfManaged")]
    [InlineData("Wolverine")]
    public async Task SameBusinessServiceCommitsAndConsumesAcrossProviders(string provider)
    {
        using var host = await fixture.CreateHostAsync(provider);
        await host.StartAsync();
        try
        {
            Guid id;
            using (var scope = host.Services.CreateScope())
            {
                var result = await scope.ServiceProvider.GetRequiredService<ProfileService>().CreateAsync(
                    new("https://identity.example", Guid.NewGuid().ToString(), "中文档案", ["reader"]),
                    new("https://identity.example", "operator"));
                Assert.True(result.IsRight);
                id = result.Right.Id;
            }
            await MessagingRecoveryFixture.WaitForAuditAsync(host, id);
            using var check = host.Services.CreateScope();
            var context = check.ServiceProvider.GetRequiredService<ReferenceDbContext>();
            Assert.Equal("中文档案", (await context.Profiles.SingleAsync()).DisplayName);
            Assert.Equal(id, (await context.Audit.SingleAsync()).ProfileId);
        }
        finally { await host.StopAsync(); }
    }

    [MessagingIntegrationTheory]
    [InlineData("SelfManaged")]
    [InlineData("Wolverine")]
    public async Task FailedUnitRollsBackBusinessAndOutgoingEvent(string provider)
    {
        using var host = await fixture.CreateHostAsync(provider);
        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
            var id = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(async token =>
            {
                var profile = UserProfile.Create("https://identity.example", id.ToString(), "rollback", ["reader"], DateTimeOffset.UtcNow);
                context.Add(profile);
                await publisher.EnqueueAsync(new UserProfileChangedV1(id, DateTimeOffset.UtcNow, profile.Id,
                    1, "created", "https://identity.example", "operator"), token);
                throw new InvalidOperationException("intentional rollback");
            }));
            Assert.Empty(await context.Profiles.ToListAsync());
            Assert.Empty(await context.Audit.ToListAsync());
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IFailedMessageOperations>().ListAsync(100));
        }
        finally { await host.StopAsync(); }
    }
}
