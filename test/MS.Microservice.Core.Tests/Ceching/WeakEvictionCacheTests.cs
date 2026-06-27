using System;
using FluentAssertions;
using MS.Microservice.Core.Ceching;
using Xunit;

namespace MS.Microservice.Core.Tests.Ceching;

public sealed class WeakEvictionCacheTests
{
    private sealed record Holder(string Name);

    [Fact]
    public void Add_ShouldThrow_WhenValueIsNull()
    {
        var cache = new WeakEvictionCache<string, Holder>(TimeSpan.Zero);
        Assert.Throws<ArgumentNullException>(() => cache.Add("key", null!));
    }

    [Fact]
    public void Add_TryGet_ReturnsValue()
    {
        var cache = new WeakEvictionCache<string, Holder>(TimeSpan.FromMinutes(5));
        var value = new Holder("demo");
        cache.Add("key", value);

        var found = cache.TryGet("key", out var retrieved);

        found.Should().BeTrue();
        retrieved.Should().Be(value);
    }

    [Fact]
    public void TryGet_ShouldReturnFalse_WhenKeyDoesNotExist()
    {
        var cache = new WeakEvictionCache<string, Holder>(TimeSpan.Zero);
        var found = cache.TryGet("missing", out Holder? value);

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void DoWeakEviction_ShouldKeepLiveReferenceAccessible()
    {
        var cache = new WeakEvictionCache<string, Holder>(TimeSpan.Zero);
        var value = new Holder("demo");
        cache.Add("key", value);

        cache.DoWeakEviction();
        var found = cache.TryGet("key", out Holder? restored);

        found.Should().BeTrue();
        restored.Should().BeSameAs(value);
    }

    [Fact]
    public void DoWeakEviction_BeforeThreshold_KeepsStrong()
    {
        var cache = new WeakEvictionCache<string, Holder>(TimeSpan.FromHours(1));
        var value = new Holder("persist");
        cache.Add("key", value);

        cache.DoWeakEviction();

        cache.TryGet("key", out var retrieved).Should().BeTrue();
        retrieved.Should().BeSameAs(value);
    }

    [Fact]
    public void MultipleAdds_TryGetEach_ReturnsCorrectValues()
    {
        var cache = new WeakEvictionCache<string, string>(TimeSpan.FromMinutes(5));
        cache.Add("k1", "v1");
        cache.Add("k2", "v2");
        cache.Add("k3", "v3");

        cache.TryGet("k1", out var r1).Should().BeTrue();
        r1.Should().Be("v1");
        cache.TryGet("k2", out var r2).Should().BeTrue();
        r2.Should().Be("v2");
        cache.TryGet("k3", out var r3).Should().BeTrue();
        r3.Should().Be("v3");
    }

    [Fact]
    public void TryGet_NotFound_ThenAdd_FindsIt()
    {
        var cache = new WeakEvictionCache<string, Holder>(TimeSpan.FromMinutes(5));
        cache.TryGet("key", out _).Should().BeFalse();

        var value = new Holder("new");
        cache.Add("key", value);
        cache.TryGet("key", out var retrieved).Should().BeTrue();
        retrieved.Should().Be(value);
    }
}
