using System;

namespace MS.Microservice.Core.Common.Advance.Resilience
{
    public class RetryContext
    {
        public int Attempt { get; internal set; } = 1;
        public DateTime StartTime { get; internal set; } = DateTime.UtcNow;
        public Exception? LastException { get; internal set; }
        public TimeSpan TotalElapsed => DateTime.UtcNow - StartTime;
    }
}
