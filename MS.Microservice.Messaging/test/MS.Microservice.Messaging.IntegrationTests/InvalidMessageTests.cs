using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Messaging.RabbitMQ;
using RabbitMQ.Client;

namespace MS.Microservice.Messaging.IntegrationTests;

[Collection(MessagingRecoveryCollection.Name)]
public sealed class InvalidMessageTests(MessagingRecoveryFixture fixture)
{
    [MessagingIntegrationTheory]
    [InlineData("SelfManaged", "version")]
    [InlineData("SelfManaged", "json")]
    [InlineData("SelfManaged", "identity")]
    [InlineData("Wolverine", "version")]
    [InlineData("Wolverine", "json")]
    [InlineData("Wolverine", "identity")]
    public async Task InvalidMessagesAreDiagnosableWithoutBusinessEffects(string provider, string defect)
    {
        var environment = await fixture.AllocateEnvironmentAsync(provider);
        using var host = await ProbeHost.CreateAsync(environment, new());
        try
        {
            var message = ProbeHost.Topology().Registry.Serialize(ProbeHost.Message());
            var version = defect == "version" ? 99 : 1;
            var frameId = defect == "identity" ? Guid.NewGuid() : message.Id;
            var properties = new BasicProperties
            {
                MessageId = frameId.ToString("N"), ContentType = "application/json", Type = $"probe.changed.v{version}",
                Persistent = true,
                Headers = new Dictionary<string, object?>
                {
                    ["ms-contract-name"] = "probe.changed", ["ms-contract-version"] = version,
                    ["ms-occurred-at"] = message.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture)
                }
            };
            // Route to the subscribed queue while deliberately violating its message contract.
            await host.Services.GetRequiredService<RabbitMqTransport>().SendEnvelopeConfirmedAsync(
                "probe.changed.v1", properties, Encoding.UTF8.GetBytes(defect == "json" ? "{" : message.Payload), default);
            await MessagingRecoveryFixture.UntilAsync(async token =>
            {
                using var scope = host.Services.CreateScope();
                return (await scope.ServiceProvider.GetRequiredService<IFailedMessageOperations>().ListAsync(100, token))
                    .Any(failure => failure.MessageId == frameId && !string.IsNullOrWhiteSpace(failure.ErrorCode));
            });
            using var check = host.Services.CreateScope();
            Assert.Empty(await check.ServiceProvider.GetRequiredService<ProbeContext>().Receipts.ToListAsync());
        }
        finally { await host.StopAsync(); }
    }
}
