# Reliable messaging contracts

Business code references this package, not a broker or a persistence provider. Events carry a stable `Id` and UTC occurrence time. Register each CLR type under an explicit name/version; readers only accept registered contracts. Registration checks the complete UTF-8 routing key before any message is stored; see [contract routing key](../../docs/contract-routing-key.md).

`IIntegrationEventPublisher.EnqueueAsync` stages a serialized snapshot in the active `IUnitOfWork.ExecuteAsync` operation. It does not send over the network. A successful outer unit of work commits business changes and pending messages together. Handlers track business changes and enqueue follow-up events without committing their own transactions.

Provider implementations own recovery and their private Inbox/Outbox tables. `IMessageTransport` is an extension point whose successful completion means positive broker confirmation. `IMessageReceiver` returns an explicit acknowledgment, requeue, or rejection decision. An occupied lease must never produce a successful acknowledgment.

A component replacement must preserve identity, transaction rollback, recovery, and consumer effects. It need not reproduce another provider's tables or internal status values. Copy this project without any dependency on a Reference or Lab project, or build a local package with `dotnet pack -c Release`.

Failure identifiers are provider-owned opaque values. A missing failure timestamp is represented by null; adapters must not invent one. Provider registration uses a name so an additional implementation can enforce exclusivity without adding a value to a shared enum. The reference host exposes the two implementations it has configured explicitly.
