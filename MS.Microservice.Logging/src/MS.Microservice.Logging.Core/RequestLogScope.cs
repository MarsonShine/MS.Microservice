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
        var currentState = new ScopeState(context);
        CurrentState.Value = currentState;
        return new PopWhenDisposed(currentState, priorState);
    }

    private sealed class ScopeState(RequestLogContext context)
    {
        private RequestLogContext? _context = context;

        public RequestLogContext? Context => Volatile.Read(ref _context);

        public void Clear() => Volatile.Write(ref _context, null);
    }

    private sealed class PopWhenDisposed(ScopeState currentState, ScopeState? priorState) : IDisposable
    {
        private ScopeState? _currentState = currentState;
        private ScopeState? _priorState = priorState;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // Child execution contexts can still hold this state after the current flow exits.
            _currentState!.Clear();
            CurrentState.Value = _priorState;
            _currentState = null;
            _priorState = null;
        }
    }
}
