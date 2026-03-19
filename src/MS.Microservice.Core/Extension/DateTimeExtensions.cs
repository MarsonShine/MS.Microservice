using System;

namespace MS.Microservice.Core.Extension
{
    public static partial class DateTimeExtensions
    {
        extension(DateTimeOffset dateTime)
        {
            public long ToLocalTimeMilliseconds()
            {
                TimeSpan duration = dateTime - DateTimeOffset.UnixEpoch;
                return duration.Ticks / TimeSpan.TicksPerMillisecond;
            }

            public long ToLocalTimeSeconds()
            {
                TimeSpan duration = dateTime - DateTimeOffset.UnixEpoch;
                return duration.Ticks / TimeSpan.TicksPerSecond;
            }
        }

        extension(long localTimestamp)
        {
            public DateTimeOffset FromUnixTimeMilliseconds() => DateTimeOffset.FromUnixTimeMilliseconds(localTimestamp);
            public DateTimeOffset FromUnixTimeSeconds() => DateTimeOffset.FromUnixTimeSeconds(localTimestamp);
        }
    }
}
