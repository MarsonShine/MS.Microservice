using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.RabbitMQ;
using System.Text.Json;

namespace MS.Microservice.Messaging.IntegrationTests;

[Collection(MessagingRecoveryCollection.Name)]
public sealed class SharedContractMatrixTests(MessagingRecoveryFixture fixture)
{
    [MessagingIntegrationTheory]
    [InlineData("SelfManaged", 0)]
    [InlineData("SelfManaged", 1)]
    [InlineData("SelfManaged", 2)]
    [InlineData("Wolverine", 0)]
    [InlineData("Wolverine", 1)]
    [InlineData("Wolverine", 2)]
    public async Task DataAndIdentitySurviveBothProvidersAndIndependentSubscriptions(string provider, int variant)
    {
        var environment = await fixture.AllocateEnvironmentAsync(provider);
        using var host = await ProbeHost.CreateAsync(environment, new(), twoConsumers: true);
        try
        {
            var message = ProbeHost.Message(variant);
            await ProbeHost.EnqueueAsync(host, message);
            await ProbeHost.WaitForReceiptsAsync(host, 2);
            await fixture.WaitForDrainedAsync(environment, host);
            using var scope = host.Services.CreateScope();
            var receipts = await scope.ServiceProvider.GetRequiredService<ProbeContext>().Receipts.ToListAsync();
            Assert.Equal(new[] { "primary", "secondary" }, receipts.Select(x => x.Consumer).Order().ToArray());
            foreach (var receipt in receipts)
            {
                Assert.Equal(message.Id, receipt.MessageId);
                var received = JsonSerializer.Deserialize<ProbeEvent>(receipt.Payload, ProbeHost.Json)!;
                Assert.Equal(JsonSerializer.Serialize(message, ProbeHost.Json), JsonSerializer.Serialize(received, ProbeHost.Json));
            }
        }
        finally { await host.StopAsync(); }
    }

    [MessagingIntegrationTheory]
    [InlineData("SelfManaged")]
    [InlineData("Wolverine")]
    public async Task FailedConsumptionRollsBackFollowUpAndReplayRetainsIdentity(string provider)
    {
        var environment = await fixture.AllocateEnvironmentAsync(provider);
        var message = ProbeHost.Message();
        var behavior = new ProbeBehavior { FailingId = message.Id, EmitFollowUp = true };
        using var host = await ProbeHost.CreateAsync(environment, behavior);
        try
        {
            await ProbeHost.EnqueueAsync(host, message);
            FailedMessage? failed = null;
            await MessagingRecoveryFixture.UntilAsync(async token =>
            {
                using var scope = host.Services.CreateScope();
                failed = (await scope.ServiceProvider.GetRequiredService<IFailedMessageOperations>().ListAsync(100, token)).SingleOrDefault();
                return failed is not null;
            });
            using (var scope = host.Services.CreateScope())
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<ProbeContext>().Receipts.ToListAsync());
            Assert.Equal(message.Id, failed!.MessageId);
            behavior.FailingId = Guid.Empty;
            using (var scope = host.Services.CreateScope())
                Assert.Equal(ReplayResult.Accepted, await scope.ServiceProvider.GetRequiredService<IFailedMessageOperations>().ReplayAsync(failed.FailureId));
            await ProbeHost.WaitForReceiptsAsync(host, 2);
            await fixture.WaitForDrainedAsync(environment, host);
            using var check = host.Services.CreateScope();
            var ids = await check.ServiceProvider.GetRequiredService<ProbeContext>().Receipts.Select(x => x.MessageId).ToListAsync();
            Assert.Contains(message.Id, ids);
            Assert.Contains(message.FollowUpId, ids);
        }
        finally { await host.StopAsync(); }
    }

    [MessagingIntegrationTheory]
    [InlineData("SelfManaged")]
    [InlineData("Wolverine")]
    public async Task PoisonMessageDoesNotBlockHealthyWorkAndTransientFailuresRecover(string provider)
    {
        var environment = await fixture.AllocateEnvironmentAsync(provider);
        var poison = ProbeHost.Message();
        var healthy = ProbeHost.Message();
        var behavior = new ProbeBehavior { FailingId = poison.Id };
        using var host = await ProbeHost.CreateAsync(environment, behavior);
        try
        {
            await ProbeHost.EnqueueAsync(host, poison, healthy);
            await ProbeHost.WaitForReceiptsAsync(host, 1);
            await MessagingRecoveryFixture.UntilAsync(async token =>
            {
                using var scope = host.Services.CreateScope();
                return (await scope.ServiceProvider.GetRequiredService<IFailedMessageOperations>().ListAsync(100, token))
                    .Any(failure => failure.MessageId == poison.Id);
            });
            var retry = ProbeHost.Message();
            behavior.FailingId = retry.Id;
            behavior.TransientFailures = 2;
            await ProbeHost.EnqueueAsync(host, retry);
            await ProbeHost.WaitForReceiptsAsync(host, 2);
            Assert.Equal(3, behavior.Attempts);
            using var check = host.Services.CreateScope();
            var rows = await check.ServiceProvider.GetRequiredService<ProbeContext>().Receipts.ToListAsync();
            Assert.DoesNotContain(rows, row => row.MessageId == poison.Id);
            Assert.Contains(rows, row => row.MessageId == healthy.Id);
            Assert.Contains(rows, row => row.MessageId == retry.Id);
        }
        finally { await host.StopAsync(); }
    }

    [MessagingIntegrationTheory]
    [InlineData("SelfManaged")]
    [InlineData("Wolverine")]
    public async Task ConcurrentPhysicalDuplicatesHaveOneBusinessEffect(string provider)
    {
        var environment = await fixture.AllocateEnvironmentAsync(provider);
        using var host = await ProbeHost.CreateAsync(environment, new());
        try
        {
            var message = ProbeHost.Message();
            var transport = host.Services.GetRequiredService<RabbitMqTransport>();
            var serialized = ProbeHost.Topology().Registry.Serialize(message);
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => transport.SendConfirmedAsync(serialized, default)));
            await ProbeHost.WaitForReceiptsAsync(host, 1);
            // A final distinct marker proves the queue can still make progress after the duplicates.
            var marker = ProbeHost.Message();
            await ProbeHost.EnqueueAsync(host, marker);
            await ProbeHost.WaitForReceiptsAsync(host, 2);
            await fixture.WaitForDrainedAsync(environment, host);
            using var check = host.Services.CreateScope();
            Assert.Equal(1, await check.ServiceProvider.GetRequiredService<ProbeContext>().Receipts.CountAsync(x => x.MessageId == message.Id));
        }
        finally { await host.StopAsync(); }
    }
}
