using System;

namespace MS.Microservice.Core.Common.Advance.Resilience
{
    public class RetryContext
    {
        private readonly TimeProvider _clock;
        private readonly long _startedTimestamp;

        public RetryContext() : this(TimeProvider.System) { }

        internal RetryContext(TimeProvider clock)
        {
            ArgumentNullException.ThrowIfNull(clock);
            _clock = clock;
            _startedTimestamp = clock.GetTimestamp();
            StartTime = clock.GetUtcNow().UtcDateTime;
        }

        public int Attempt { get; internal set; } = 1;
        public DateTime StartTime { get; internal set; }
        public Exception? LastException { get; internal set; }
        public TimeSpan TotalElapsed => _clock.GetElapsedTime(_startedTimestamp);
    }
}
