using System;

namespace MS.Microservice.Core.Extension
{
    public static class DateTimeExtensions
    {
        public static long ToLocalTimeMilliseconds(this DateTimeOffset dateTime)
        {
            TimeSpan duration = dateTime - DateTimeOffset.UnixEpoch;
            return duration.Ticks / TimeSpan.TicksPerMillisecond;
        }

        public static long ToLocalTimeSeconds(this DateTimeOffset dateTime)
        {
            TimeSpan duration = dateTime - DateTimeOffset.UnixEpoch;
            return duration.Ticks / TimeSpan.TicksPerSecond;
        }

        public static DateTimeOffset FromUnixTimeMilliseconds(this long localTimestamp) => DateTimeOffset.FromUnixTimeMilliseconds(localTimestamp);
        public static DateTimeOffset FromUnixTimeSeconds(this long localTimestamp) => DateTimeOffset.FromUnixTimeSeconds(localTimestamp);
    }
}
