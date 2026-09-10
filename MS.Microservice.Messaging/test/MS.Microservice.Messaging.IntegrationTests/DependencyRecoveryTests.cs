using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.FaultWorker;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;

namespace MS.Microservice.Messaging.IntegrationTests;

[Collection(MessagingRecoveryCollection.Name)]
public sealed class DependencyRecoveryTests(MessagingRecoveryFixture fixture)
{
    [MessagingIntegrationTheory]
    [InlineData("SelfManaged")]
    [InlineData("Wolverine")]
    public async Task BrokerOutageAllowsDurableBusinessSaveAndRecoversWithoutRestart(string provider)
    {
        var environment = await fixture.CreateEnvironmentAsync(provider);
        using var host = FaultHost.Build(environment);
        await host.StartAsync();
        var stopped = false;
        Guid id;
        try
        {
            await fixture.StopBrokerAsync();
            stopped = true;
            using (var scope = host.Services.CreateScope())
            {
                var result = await scope.ServiceProvider.GetRequiredService<ProfileService>().CreateAsync(
                    new("https://identity.example", Guid.NewGuid().ToString(), "Broker outage", ["reader"]),
                    new("https://identity.example", "operator"));
                Assert.True(result.IsRight);
                id = result.Right.Id;
                Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ReferenceDbContext>().Profiles.CountAsync());
            }
            await fixture.StartBrokerAsync();
            stopped = false;
            await MessagingRecoveryFixture.WaitForAuditAsync(host, id);
            await fixture.WaitForDrainedAsync(environment, host);
        }
        finally
        {
            if (stopped) await fixture.StartBrokerAsync();
            await host.StopAsync();
        }
    }

    [MessagingIntegrationTheory]
    [InlineData("SelfManaged")]
    [InlineData("Wolverine")]
    public async Task DatabaseOutageRejectsWritesAndSameHostRecovers(string provider)
    {
        var environment = await fixture.CreateEnvironmentAsync(provider);
        using var host = FaultHost.Build(environment);
        await host.StartAsync();
        var stopped = false;
        try
        {
            await fixture.StopDatabaseAsync();
            stopped = true;
            using (var scope = host.Services.CreateScope())
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<ProfileService>().CreateAsync(
                    new("https://identity.example", "failed-write", "Database outage", ["reader"]),
                    new("https://identity.example", "operator"), timeout.Token));
            }
            await fixture.StartDatabaseAsync();
            stopped = false;
            Guid id;
            using (var scope = host.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
                Assert.Empty(await context.Profiles.ToListAsync());
                var result = await scope.ServiceProvider.GetRequiredService<ProfileService>().CreateAsync(
                    new("https://identity.example", "recovered", "Recovered", ["reader"]),
                    new("https://identity.example", "operator"));
                Assert.True(result.IsRight);
                id = result.Right.Id;
            }
            await MessagingRecoveryFixture.WaitForAuditAsync(host, id);
            await fixture.WaitForDrainedAsync(environment, host);
        }
        finally
        {
            if (stopped) await fixture.StartDatabaseAsync();
            await host.StopAsync();
        }
    }
}
