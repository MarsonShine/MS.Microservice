using MS.Microservice.Persistence.EFCore.DbContext;
using Xunit;

namespace MS.Microservice.Core.Tests.Architecture;

public sealed class PersistenceBoundaryTests
{
    [Fact]
    public void ReusableEfCoreHelpersDoNotReferenceSampleModelsOrHosts()
    {
        var references = typeof(EfCoreQueryableExtensions).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name!.StartsWith("MS.Microservice.Lab", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Name!.StartsWith("MS.Microservice.Reference", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Name == "MS.Microservice.Domain");
    }
}
