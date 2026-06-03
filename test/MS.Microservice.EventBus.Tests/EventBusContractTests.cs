using MS.Microservice.EventBus.Abstractions;

namespace MS.Microservice.EventBus.Tests;

public class EventBusContractTests
{
    [Fact]
    public void LegacyInterface_ShouldRemainAssignableToCanonicalInterface()
    {
        Assert.True(typeof(IEventBus).IsAssignableFrom(typeof(IEventbus)));
    }
}
