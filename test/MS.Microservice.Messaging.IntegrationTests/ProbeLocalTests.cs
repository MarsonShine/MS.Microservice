using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.SelfManaged;

namespace MS.Microservice.Messaging.IntegrationTests;

public sealed class ProbeLocalTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public async Task SharedProbeFixtureExercisesTransactionalFollowUpWithoutExternalServices(int variant, bool fail)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var message = ProbeHost.Message(variant);
        var services = new ServiceCollection();
        services.AddDbContext<SelfProbeContext>(options => options.UseSqlite(connection));
        services.AddScoped<ProbeContext>(provider => provider.GetRequiredService<SelfProbeContext>());
        services.AddSingleton(new ProbeBehavior { EmitFollowUp = true, FailingId = fail ? message.Id : Guid.Empty });
        services.AddSelfManagedMessaging<SelfProbeContext>(ProbeHost.Topology());
        await using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SelfProbeContext>().Database.EnsureCreatedAsync();
        var result = await provider.GetRequiredService<IMessageReceiver>().ReceiveAsync(
            ProbeHost.Topology().Registry.Serialize(message), "primary", default);
        Assert.Equal(fail ? DeliveryResult.Reject : DeliveryResult.Acknowledge, result);
        using var check = provider.CreateScope();
        Assert.Equal(fail ? 0 : 1, await check.ServiceProvider.GetRequiredService<ProbeContext>().Receipts.CountAsync());
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Outbox";
        Assert.Equal(fail ? 0 : 1, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }
}
