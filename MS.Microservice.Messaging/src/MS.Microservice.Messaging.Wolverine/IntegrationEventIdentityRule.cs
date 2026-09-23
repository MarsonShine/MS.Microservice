using System.Globalization;
using global::Wolverine;

namespace MS.Microservice.Messaging.Wolverine;

public sealed class IntegrationEventIdentityRule(MessageContractRegistry registry) : IEnvelopeRule
{
    public void Modify(Envelope envelope)
    {
        if (envelope.Message is not IIntegrationEvent message) return;
        var contract = registry.Get(message.GetType());
        if (message.Id == Guid.Empty) throw new MessageContractException("An integration event must have a stable Id.");
        envelope.Id = message.Id;
        envelope.MessageType = MessageRoutingKey.Format(contract.Name, contract.Version);
        envelope.Headers["ms-contract-name"] = contract.Name;
        envelope.Headers["ms-contract-version"] = contract.Version.ToString(CultureInfo.InvariantCulture);
        envelope.Headers["ms-occurred-at"] = message.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture);
    }
}
