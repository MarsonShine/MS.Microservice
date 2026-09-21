using System.Linq.Expressions;
using MS.Microservice.Lab.AotExamples.Static.Linq;
using Xunit;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class ExpressionCompilationTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public void QueryCompositionAndExplicitDelegatePreservePredicate(int value, bool expected)
    {
        var legacy = new Legacy.Linq.ExpressionStarter<int>();
        legacy.And(x => x > 0);
        legacy.And(x => x < 10);
        Func<int, bool> compiled = legacy;
        Assert.Equal(expected, compiled(value));
        var query = PredicateBuilder.New<int>();
        query.And(x => x > 0);
        query.And(x => x < 10);
        Expression<Func<int, bool>> expression = query;
        Assert.Equal(expected, expression.Compile(preferInterpretation: true)(value));
        Assert.Equal(expected, MemoryFilter.Select(new[] { value }, static x => x > 0 && x < 10).Any());
    }
}
