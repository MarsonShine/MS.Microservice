using System.Threading;

namespace MS.Microservice.Logging.Core;

/// <summary>
/// Provides an ambient, async-flowing request log context.
/// </summary>
public static class RequestLogScope
{
    private static readonly AsyncLocal<RequestLogContext?> CurrentState = new();

    public static RequestLogContext? Current => CurrentState.Value;

    public static IDisposable Push(RequestLogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var priorState = CurrentState.Value;
        CurrentState.Value = context;
        return new PopWhenDisposed(priorState);
    }

    private sealed class PopWhenDisposed(RequestLogContext? priorState) : IDisposable
    {
        private RequestLogContext? _priorState = priorState;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentState.Value = _priorState;
            _priorState = null;
        }
    }
}
