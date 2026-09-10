using System.Threading;

namespace MS.Microservice.Logging.Core;

/// <summary>
/// Provides an ambient, async-flowing request log context.
/// </summary>
public static class RequestLogScope
{
    private static readonly AsyncLocal<ScopeState?> CurrentState = new();

    public static RequestLogContext? Current => CurrentState.Value?.Context;

    public static IDisposable Push(RequestLogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var priorState = CurrentState.Value;
        CurrentState.Value = new ScopeState(context);
        return new PopWhenDisposed(priorState);
    }

    private sealed class ScopeState(RequestLogContext context)
    {
        public RequestLogContext Context { get; } = context;
    }

    private sealed class PopWhenDisposed(ScopeState? priorState) : IDisposable
    {
        private ScopeState? _priorState = priorState;
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