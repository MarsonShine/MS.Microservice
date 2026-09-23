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
    public void Push_ShouldRestoreParent_WhenSameContextIsPushedAgain()
    {
        var context = new RequestLogContext { RequestId = "reused" };

        using (RequestLogScope.Push(context))
        {
            using (RequestLogScope.Push(context))
            {
                RequestLogScope.Current.Should().BeSameAs(context);
            }

            RequestLogScope.Current.Should().BeSameAs(context);
        }

        RequestLogScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task Push_ShouldFlowAcrossAwaitAndRestoreParent()
    {
        var outer = new RequestLogContext { RequestId = "outer" };
        var inner = new RequestLogContext { RequestId = "inner" };

        using (RequestLogScope.Push(outer))
        {
            using (RequestLogScope.Push(inner))
            {
                await Task.Yield();
                RequestLogScope.Current.Should().BeSameAs(inner);
            }

            RequestLogScope.Current.Should().BeSameAs(outer);
        }

        RequestLogScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task Push_ShouldIsolateConcurrentChildFlows()
    {
        var outer = new RequestLogContext { RequestId = "outer" };
        var first = new RequestLogContext { RequestId = "first" };
        var second = new RequestLogContext { RequestId = "second" };
        var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        using (RequestLogScope.Push(outer))
        {
            var firstTask = Task.Run(() => RunChild(first));
            var secondTask = Task.Run(() => RunChild(second));
            var results = await Task.WhenAll(firstTask, secondTask);

            results.Should().OnlyContain(result => result.InsideMatchesChild && result.RestoredParent);
            RequestLogScope.Current.Should().BeSameAs(outer);
        }

        RequestLogScope.Current.Should().BeNull();

        async Task<(bool InsideMatchesChild, bool RestoredParent)> RunChild(RequestLogContext child)
        {
            bool insideMatchesChild;
            using (RequestLogScope.Push(child))
            {
                if (Interlocked.Increment(ref started) == 2)
                {
                    bothReady.SetResult();
                }

                await bothReady.Task;
                insideMatchesChild = ReferenceEquals(RequestLogScope.Current, child);
            }

            return (insideMatchesChild, ReferenceEquals(RequestLogScope.Current, outer));
        }
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
