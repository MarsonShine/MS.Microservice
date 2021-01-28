using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Timer
{
    public class StopwatchTimer : ITimer, IDisposable
    {
        private readonly IClock _clock;
        private bool _disposed;

        public StopwatchTimer(IClock clock)
        {
            _clock = clock;
        }

        public long CurrentTime() => _clock.Nanoseconds;

        public void Dispose()
        {
            Dispose(true);
        }

        public void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Free any other managed objects here.
                }
            }

            _disposed = true;
        }

        public long EndRecording() => _clock.Nanoseconds;

        public void Reset()
        {
            _clock.Reset();
        }

        public long StartRecording()
        {
            throw new NotImplementedException();
        }
    }
}
