using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace MS.Microservice.Core.Tests.Linq;

public sealed class ExpressionStarterTests
{
    [Fact]
    public void Start_ShouldSetPredicate()
    {
        var starter = new ExpressionStarter<int>();

        Expression<Func<int, bool>> expression = starter.Start(x => x > 5);

        Assert.True(starter.IsStarted);
        Assert.True(expression.Compile(preferInterpretation: true)(6));
        Assert.False(expression.Compile(preferInterpretation: true)(4));
    }

    [Fact]
    public void Start_WhenCalledTwice_ShouldThrow()
    {
        var starter = new ExpressionStarter<int>();
        starter.Start(x => x > 0);

        Assert.Throws<Exception>(() => starter.Start(x => x < 10));
    }

    [Fact]
    public void Or_And_ShouldComposeExpressions()
    {
        var starter = PredicateBuilder.New<int>();

        starter.Or(x => x > 10);
        starter.And(x => x < 20);
        Func<int, bool> predicate = ((Expression<Func<int, bool>>)starter).Compile(preferInterpretation: true);

        Assert.True(predicate(15));
        Assert.False(predicate(9));
        Assert.False(predicate(21));
    }

    [Fact]
    public void New_WithDefaultTrue_ShouldCompileToTrueBeforeStart()
    {
        Expression<Func<int, bool>> expression = PredicateBuilder.New<int>(true);
        Func<int, bool> predicate = expression.Compile(preferInterpretation: true);

        Assert.True(predicate(0));
        Assert.True(predicate(100));
    }

    [Fact]
    public void ImplicitConversion_FromExpression_ShouldCreateStartedPredicate()
    {
        ExpressionStarter<int> starter = (Expression<Func<int, bool>>)(x => x % 2 == 0);
        Expression<Func<int, bool>> expression = starter;

        Assert.True(starter.IsStarted);
        Assert.True(expression.Compile(preferInterpretation: true)(2));
        Assert.False(expression.Compile(preferInterpretation: true)(3));
    }
}
