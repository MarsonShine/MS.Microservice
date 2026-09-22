using MS.Microservice.Core.NativeAot.Smoke;

namespace MS.Microservice.Aot.Tests;

public sealed class NativeSerializationContractTests
{
    public static IEnumerable<object[]> Cases => SerializationScenarios.All.Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    public Task SharedConsumerScenario(string name)
        => SerializationScenarios.All.Single(s => s.Name == name).Run();
}
