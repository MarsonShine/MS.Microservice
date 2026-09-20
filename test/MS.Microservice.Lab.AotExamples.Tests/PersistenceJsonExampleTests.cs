using MS.Microservice.Lab.AotExamples.Static.Persistence;
using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.Persistence.JsonColumns;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class PersistenceJsonExampleTests
{
    [Fact]
    public void ExplicitContractKeepsColumnJsonAndRejectsUnknownTypes()
    {
        var value = new ColumnDocument("stored", 7);
        var columns = new JsonColumns(ColumnJsonContext.Default.ColumnDocument);
        Assert.Equal(LegacyExample.Write(value), columns.Write(value));
        Assert.Throws<NotSupportedException>(() => columns.Write(new Version(1, 2)));
    }
}
