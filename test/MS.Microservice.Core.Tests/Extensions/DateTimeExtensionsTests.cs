using MS.Microservice.Core.Extension;
using System;

namespace MS.Microservice.Core.Tests.Extensions
{
    public class DateTimeExtensionsTests
    {
        [Fact]
        public void ToLocalTimeMilliseconds_UnixEpoch_ReturnsZero()
        {
            var epoch = DateTimeOffset.UnixEpoch;
            Assert.Equal(0, epoch.ToLocalTimeMilliseconds());
        }

        [Fact]
        public void ToLocalTimeMilliseconds_KnownDate_ReturnsExpected()
        {
            // 2024-01-01T00:00:00Z
            var dto = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            long expected = (long)(dto - DateTimeOffset.UnixEpoch).TotalMilliseconds;
            Assert.Equal(expected, dto.ToLocalTimeMilliseconds());
        }

        [Fact]
        public void ToLocalTimeSeconds_UnixEpoch_ReturnsZero()
        {
            var epoch = DateTimeOffset.UnixEpoch;
            Assert.Equal(0, epoch.ToLocalTimeSeconds());
        }

        [Fact]
        public void ToLocalTimeSeconds_KnownDate_ReturnsExpected()
        {
            var dto = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            long expected = (long)(dto - DateTimeOffset.UnixEpoch).TotalSeconds;
            Assert.Equal(expected, dto.ToLocalTimeSeconds());
        }

        [Fact]
        public void FromUnixTimeMilliseconds_Zero_ReturnsEpoch()
        {
            long ms = 0;
            Assert.Equal(DateTimeOffset.UnixEpoch, ms.FromUnixTimeMilliseconds());
        }

        [Fact]
        public void FromUnixTimeMilliseconds_KnownValue_RoundTrips()
        {
            var original = new DateTimeOffset(2024, 6, 15, 12, 30, 0, TimeSpan.Zero);
            long ms = original.ToLocalTimeMilliseconds();
            var restored = ms.FromUnixTimeMilliseconds();
            Assert.Equal(original, restored);
        }

        [Fact]
        public void FromUnixTimeSeconds_Zero_ReturnsEpoch()
        {
            long s = 0;
            Assert.Equal(DateTimeOffset.UnixEpoch, s.FromUnixTimeSeconds());
        }

        [Fact]
        public void FromUnixTimeSeconds_KnownValue_RoundTrips()
        {
            var original = new DateTimeOffset(2024, 6, 15, 12, 30, 0, TimeSpan.Zero);
            long s = original.ToLocalTimeSeconds();
            var restored = s.FromUnixTimeSeconds();
            Assert.Equal(original, restored);
        }
    }
}
