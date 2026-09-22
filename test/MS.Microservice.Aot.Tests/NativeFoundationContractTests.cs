using MS.Microservice.Core.NativeAot.Smoke;

namespace MS.Microservice.Aot.Tests;

public sealed class NativeFoundationContractTests
{
    public static IEnumerable<object[]> Cases => FoundationScenarios.All.Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    public Task SharedConsumerScenario(string name)
        => FoundationScenarios.All.Single(s => s.Name == name).Run();
}
