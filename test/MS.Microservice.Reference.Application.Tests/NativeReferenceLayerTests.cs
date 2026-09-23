using MS.Microservice.Core.NativeAot.Smoke;
using Xunit;

namespace MS.Microservice.Reference.Application.Tests;

public sealed class NativeReferenceLayerTests
{
    public static IEnumerable<object[]> Cases => ReferenceScenarios.All.Select(x => new object[] { x.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    public Task SameScenariosAsNativeExecutable(string name)
        => ReferenceScenarios.All.Single(x => x.Name == name).Run();
}
