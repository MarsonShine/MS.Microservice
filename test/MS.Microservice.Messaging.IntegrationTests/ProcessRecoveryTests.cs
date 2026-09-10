using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.FaultWorker;
using MS.Microservice.Reference.Persistence;
using global::Wolverine.Runtime;

namespace MS.Microservice.Messaging.IntegrationTests;

[Collection(MessagingRecoveryCollection.Name)]
public sealed class ProcessRecoveryTests(MessagingRecoveryFixture fixture)
{
    [MessagingIntegrationTheory]
    [InlineData("SelfManaged", "save-before-commit")]
    [InlineData("SelfManaged", "save-after-commit")]
    [InlineData("SelfManaged", "consume-before-commit")]
    [InlineData("SelfManaged", "consume-after-commit")]
    [InlineData("Wolverine", "save-before-commit")]
    [InlineData("Wolverine", "save-after-commit")]
    [InlineData("Wolverine", "consume-before-commit")]
    [InlineData("Wolverine", "consume-after-commit")]
    public async Task KilledProcessRecoversOnlyCommittedEffects(string provider, string phase)
    {
        var environment = await fixture.CreateEnvironmentAsync(provider);
        var brokerStopped = false;
        Guid? outgoingId = null;
        Guid profileId;
        try
        {
            await using (var worker = new WorkerProcess(environment, phase))
            {
                await worker.SignalAsync("READY");
                if (phase == "save-after-commit")
                {
                    await fixture.StopBrokerAsync();
                    brokerStopped = true;
                }
                await worker.CommandAsync("produce");
                var signal = (await worker.SignalAsync("BARRIER")).Split('|');
                Assert.Equal(phase, signal[1]);
                profileId = Guid.Parse(signal[2]);
                using var probe = FaultHost.Build(environment);
                using var scope = probe.Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
                Assert.Equal(phase == "save-before-commit" ? 0 : 1, await context.Profiles.CountAsync());
                Assert.Equal(phase == "consume-after-commit" ? 1 : 0, await context.Audit.CountAsync());
                if (phase == "save-after-commit")
                {
                    if (provider == "Wolverine")
                        outgoingId = Assert.Single(await probe.Services.GetRequiredService<IWolverineRuntime>().Storage.Admin.AllOutgoingAsync()).Id;
                    else
                    {
                        await using var connection = new Npgsql.NpgsqlConnection(environment.ConnectionString);
                        await connection.OpenAsync();
                        await using var command = new Npgsql.NpgsqlCommand("""SELECT "Id" FROM messaging."Outbox" """, connection);
                        outgoingId = (Guid)(await command.ExecuteScalarAsync())!;
                    }
                }
                if (phase == "consume-after-commit")
                {
                    await fixture.StopBrokerAsync();
                    brokerStopped = true;
                }
                await worker.KillAsync();
            }
        }
        finally
        {
            if (brokerStopped) await fixture.StartBrokerAsync();
        }
        await using var restarted = new WorkerProcess(environment);
        await restarted.SignalAsync("READY");
        using var verification = FaultHost.Build(environment);
        if (phase != "save-before-commit") await MessagingRecoveryFixture.WaitForAuditAsync(verification, profileId);
        await fixture.WaitForDrainedAsync(environment, verification);
        using var check = verification.Services.CreateScope();
        var database = check.ServiceProvider.GetRequiredService<ReferenceDbContext>();
        Assert.Equal(phase == "save-before-commit" ? 0 : 1, await database.Profiles.CountAsync());
        Assert.Equal(phase == "save-before-commit" ? 0 : 1, await database.Audit.CountAsync());
        if (outgoingId.HasValue) Assert.Equal(outgoingId.Value, (await database.Audit.SingleAsync()).MessageId);
    }
}

public sealed class FaultProtocolTests
{
    [Fact]
    public async Task ChildSignalsItsBarrierBeforeTestTerminatesIt()
    {
        // This protocol test does not construct the container fixture or access any external service.
        await using var worker = new WorkerProcess();
        Assert.Equal("READY", await worker.SignalAsync("READY"));
        await worker.CommandAsync("enter");
        var signal = (await worker.SignalAsync("BARRIER")).Split('|');
        Assert.Equal("protocol", signal[1]);
        Assert.True(Guid.TryParse(signal[2], out _));
        await worker.KillAsync();
    }
}
