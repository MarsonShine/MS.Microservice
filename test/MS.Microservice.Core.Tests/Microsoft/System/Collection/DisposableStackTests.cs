using System;
using System.Linq;
using FluentAssertions;
using Microsoft.System.Collection;
using Xunit;

namespace MS.Microservice.Core.Tests.Microsoft.System.Collection;

public sealed class DisposableStackTests
{
    [Fact]
    public void Push_Pop_And_Dispose_ShouldManageItems()
    {
        var stack = new DisposableStack<TrackingDisposable>();
        var first = new TrackingDisposable("first");
        var second = new TrackingDisposable("second");

        stack.Push(first);
        stack.Push(second);

        stack.Count.Should().Be(2);
        stack.Pop().Should().BeSameAs(second);
        stack.Dispose();

        first.IsDisposed.Should().BeTrue();
        second.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void Dispose_EmptyStack_ShouldNotThrow()
    {
        var stack = new DisposableStack<TrackingDisposable>();

        stack.Invoking(current => current.Dispose()).Should().NotThrow();
    }

    private sealed class TrackingDisposable(string name) : IDisposable
    {
        public string Name { get; } = name;
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
