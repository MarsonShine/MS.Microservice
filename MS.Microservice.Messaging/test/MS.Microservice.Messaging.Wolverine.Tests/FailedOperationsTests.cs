using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using global::Wolverine;
using global::Wolverine.Persistence.Durability;
using global::Wolverine.Persistence.Durability.DeadLetterManagement;
using global::Wolverine.Runtime;
using Xunit;

namespace MS.Microservice.Messaging.Wolverine.Tests;

public sealed class FailedOperationsTests
{
    [Theory]
    [InlineData("available", ReplayResult.Accepted)]
    [InlineData("replaying", ReplayResult.InvalidState)]
    [InlineData("unknown-contract", ReplayResult.InvalidState)]
    [InlineData("missing", ReplayResult.NotFound)]
    public async Task ReplayUsesNativeStorageAndPreservesIdentity(string state, ReplayResult expected)
    {
        var id = Guid.NewGuid();
        var runtime = Substitute.For<IWolverineRuntime>();
        var store = Substitute.For<IMessageStore>();
        var letters = Substitute.For<IDeadLetters>();
        runtime.Storage.Returns(store);
        store.DeadLetters.Returns(letters);
        var results = new DeadLetterEnvelopeResults();
        if (state != "missing") results.Envelopes.Add(new DeadLetterEnvelope(id, null, new Envelope { Id = id },
            state == "unknown-contract" ? "profile.changed.v99" : "profile.changed.v1",
            "rabbitmq://queue/ms.reference.audit", "reference", "BusinessFailure", "private details",
            DateTimeOffset.UtcNow, state == "replaying"));
        letters.QueryAsync(Arg.Any<DeadLetterEnvelopeQuery>(), Arg.Any<CancellationToken>()).Returns(results);
        var operations = new WolverineFailedMessageOperations(runtime, RegistrationTests.Topology(),
            NullLogger<WolverineFailedMessageOperations>.Instance);
        Assert.Equal(expected, await operations.ReplayAsync($"wolverine:{id:N}"));
        if (expected == ReplayResult.Accepted)
            await letters.Received(1).ReplayAsync(Arg.Is<DeadLetterEnvelopeQuery>(x => x.MessageIds.Single() == id), Arg.Any<CancellationToken>());
        else await letters.DidNotReceive().ReplayAsync(Arg.Any<DeadLetterEnvelopeQuery>(), Arg.Any<CancellationToken>());
        var failures = await operations.ListAsync();
        foreach (var failure in failures)
        {
            Assert.Null(failure.FailedAtUtc);
            Assert.Equal("BusinessFailure", failure.ErrorCode);
        }
    }
}
