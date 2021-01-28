using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core
{
    public interface IClock
    {
        event EventHandler Advanced;

        long Nanoseconds { get; }

        long Seconds { get; }

        DateTime UtcDateTime { get; }

        void Advance(TimeUnit unit, long value);

        string FormatTimestamp(DateTime timestamp);
        void Reset();
    }

    public enum TimeUnit
    {
        Nanoseconds = 0,
        Microseconds = 1,
        Milliseconds = 2,
        Seconds = 3,
        Minutes = 4,
        Hours = 5,
        Days = 6
    }
}
