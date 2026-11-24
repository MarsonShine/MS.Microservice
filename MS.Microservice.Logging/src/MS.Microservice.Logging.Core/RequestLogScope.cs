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

        var currentState = new ScopeState(context, CurrentState.Value);
        CurrentState.Value = currentState;
        return currentState;
    }

    private sealed class ScopeState(RequestLogContext context, ScopeState? priorState) : IDisposable
    {
        private RequestLogContext? _context = context;
        private ScopeState? _priorState = priorState;

        public RequestLogContext? Context => Volatile.Read(ref _context);

        public void Dispose()
        {
            // Clearing the shared context also elects the one flow that restores the parent.
            if (Interlocked.Exchange(ref _context, null) is null)
            {
                return;
            }

            CurrentState.Value = _priorState;
            _priorState = null;
        }
    }
}
