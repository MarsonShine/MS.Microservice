using System.Text;
using FluentAssertions;

namespace MS.Microservice.Core.Tests
{
    public class StringBuilderCacheTests
    {
        [Fact]
        public void Acquire_ReturnsStringBuilder()
        {
            var sb = StringBuilderCache.Acquire();
            sb.Should().NotBeNull();
            sb.Length.Should().Be(0);
            StringBuilderCache.Release(sb);
        }

        [Fact]
        public void Acquire_WithCapacity_ReturnsStringBuilderWithSufficientCapacity()
        {
            var sb = StringBuilderCache.Acquire(100);
            sb.Should().NotBeNull();
            sb.Capacity.Should().BeGreaterThanOrEqualTo(100);
            StringBuilderCache.Release(sb);
        }

        [Fact]
        public void GetStringAndRelease_ReturnsBuiltString()
        {
            var sb = StringBuilderCache.Acquire();
            sb.Append("hello world");
            var result = StringBuilderCache.GetStringAndRelease(sb);
            result.Should().Be("hello world");
        }

        [Fact]
        public void Acquire_ReusesCachedInstance()
        {
            var sb1 = StringBuilderCache.Acquire(16);
            StringBuilderCache.Release(sb1);

            var sb2 = StringBuilderCache.Acquire(16);
            // Should reuse the same instance
            sb2.Should().BeSameAs(sb1);
            StringBuilderCache.Release(sb2);
        }

        [Fact]
        public void Acquire_LargeCapacity_DoesNotCacheOnRelease()
        {
            // 大于 MAX_BUILDER_SIZE (360) 的不应被缓存
            var sb1 = StringBuilderCache.Acquire(500);
            StringBuilderCache.Release(sb1);

            var sb2 = StringBuilderCache.Acquire(500);
            // 大容量不被缓存，所以应是不同的实例
            sb2.Should().NotBeSameAs(sb1);
        }

        [Fact]
        public void Acquire_ClearsStringBuilder_BeforeReuse()
        {
            var sb = StringBuilderCache.Acquire();
            sb.Append("dirty data");
            StringBuilderCache.Release(sb);

            var reused = StringBuilderCache.Acquire();
            reused.Length.Should().Be(0);
            StringBuilderCache.Release(reused);
        }
    }
}
