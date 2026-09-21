using System.Linq.Expressions;
using MS.Microservice.Lab.AotExamples.Static.Persistence;
using LegacyExample = MS.Microservice.Lab.AotExamples.Legacy.Persistence.SoftDeleteRegistration;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class SoftDeleteRegistrationExampleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("2000-01-01")]
    [InlineData("2100-01-01")]
    public void TypedRegistrationPreservesPredicateWithoutDynamicGenericConstruction(string? deletedAt)
    {
        var row = new Row(deletedAt is null ? null : DateTime.Parse(deletedAt, System.Globalization.CultureInfo.InvariantCulture));
        var legacy = (Expression<Func<Row, bool>>)LegacyExample.Filter(typeof(Row));
        var current = SoftDeleteRegistration.Filter<Row>();
        // Interpretation is only for this comparison; production passes the expression directly to EF.
        Assert.Equal(legacy.Compile(preferInterpretation: true)(row), current.Compile(preferInterpretation: true)(row));
        Assert.Equal(deletedAt is null, current.Compile(preferInterpretation: true)(row));
    }

    private sealed record Row(DateTime? DeletedAt) : ISoftDeleteExample;
}
