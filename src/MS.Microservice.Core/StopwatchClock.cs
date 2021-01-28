using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core
{
    public class StopwatchClock : IClock
    {
        private static readonly long Factor = 1000L * 1000L * 1000L / Stopwatch.Frequency;

#pragma warning disable 67
        public event EventHandler? Advanced;
#pragma warning restore 67

        public long Nanoseconds => Stopwatch.GetTimestamp() * Factor;

        public long Seconds => TimeUnit.Nanoseconds.ToSeconds(Nanoseconds);

        public DateTime UtcDateTime => DateTime.UtcNow;

        public void Advance(TimeUnit unit, long value)
        {
            // DEVNOTE: Use test clock to advance the timer for testing purposes
        }

        public string FormatTimestamp(DateTime timestamp) { return timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffK", CultureInfo.InvariantCulture); }

        public void Reset()
        {
            
        }
    }
}
