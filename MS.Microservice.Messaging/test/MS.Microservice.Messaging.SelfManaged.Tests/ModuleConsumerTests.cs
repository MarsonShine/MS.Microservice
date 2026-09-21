using Xunit;

namespace MS.Microservice.Messaging.SelfManaged.Tests;

public sealed class ModuleConsumerTests
{
    [Fact]
    public Task SourceConsumerRegistersStaticContractsForTwoIndependentContexts()
        => global::MessagingConsumer.VerifyAsync();
}
