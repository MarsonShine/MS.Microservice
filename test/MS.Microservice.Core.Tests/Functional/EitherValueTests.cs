using FluentAssertions;
using MS.Microservice.Core.Functional;
using Xunit;

namespace MS.Microservice.Core.Tests.Functional;

public sealed class EitherValueTests
{
    [Fact]
    public void Equals_And_Operators_ShouldWorkForRightValues()
    {
        Either<Error, int> left = F.Right(42);
        Either<Error, int> right = F.Right(42);
        Either<Error, int> different = F.Right(41);

        left.Equals(right).Should().BeTrue();
        (left == right).Should().BeTrue();
        (left != different).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
        left.ToString().Should().Be("Right(42)");
    }

    [Fact]
    public void Equals_And_Operators_ShouldWorkForLeftValues()
    {
        var error = Error.Validation("bad input");
        Either<Error, int> left = F.Left(error);
        Either<Error, int> same = F.Left(error);
        Either<Error, int> right = F.Right(1);

        left.Equals(same).Should().BeTrue();
        (left == same).Should().BeTrue();
        (left != right).Should().BeTrue();
        left.ToString().Should().Contain("Left");
    }

    [Fact]
    public void Match_ShouldInvokeCorrectBranch()
    {
        Either<Error, int> success = F.Right(21);
        Either<Error, int> failure = F.Left(Error.Validation("invalid"));

        success.Match(_ => -1, value => value * 2).Should().Be(42);
        failure.Match(error => error.Code, _ => "ok").Should().Be("validation");
    }

    [Fact]
    public void LeftAndRightContainers_ShouldFormatValue()
    {
        F.Left(Error.Validation("broken")).ToString().Should().Contain("Left");
        F.Right(42).ToString().Should().Be("Right(42)");
    }
}
