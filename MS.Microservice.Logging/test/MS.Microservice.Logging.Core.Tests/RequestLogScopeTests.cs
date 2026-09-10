using FluentAssertions;
using Xunit;

namespace MS.Microservice.Logging.Core.Tests;

public sealed class RequestLogScopeTests
{
    [Fact]
    public void Push_ShouldExposeCurrentContextInsideScope()
    {
        var context = new RequestLogContext { RequestId = "req-001" };

        using (RequestLogScope.Push(context))
        {
            RequestLogScope.Current.Should().BeSameAs(context);
        }

        RequestLogScope.Current.Should().BeNull();
    }

    [Fact]
    public void Push_ShouldRestoreParentContext_WhenScopesAreNested()
    {
        var outerContext = new RequestLogContext { RequestId = "outer" };
        var innerContext = new RequestLogContext { RequestId = "inner" };

        using (RequestLogScope.Push(outerContext))
        {
            using (RequestLogScope.Push(innerContext))
            {
                RequestLogScope.Current.Should().BeSameAs(innerContext);
            }

            RequestLogScope.Current.Should().BeSameAs(outerContext);
        }

        RequestLogScope.Current.Should().BeNull();
    }

    [Fact]
    public void Dispose_ShouldBeIdempotent()
    {
        var context = new RequestLogContext { RequestId = "req-002" };
        var scope = RequestLogScope.Push(context);

        scope.Dispose();
        scope.Dispose();

        RequestLogScope.Current.Should().BeNull();
    }

    [Fact]
    public void Push_ShouldThrow_WhenContextIsNull()
    {
        var action = () => RequestLogScope.Push(null!);

        action.Should().Throw<ArgumentNullException>();
    }
}