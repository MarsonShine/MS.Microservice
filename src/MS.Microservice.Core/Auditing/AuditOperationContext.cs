using System.Text;

namespace MS.Microservice.Core.Auditing;

/// <summary>Declares a business operation name for a host auditing adapter.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class AuditOperationAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public sealed record AuditOperationInfo(Guid Id, string Scope, string Actor, string Name, string? CorrelationId);

/// <summary>Scoped operation identity shared by nested calls; do not share it between concurrent operations.</summary>
public sealed class AuditOperationContext
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private int _depth;
    private int _sequence;
    public AuditOperationInfo? Current { get; private set; }

    public IDisposable Begin(string scope, string actor, string name, Guid? operationId = null, string? correlationId = null)
    {
        Validate(scope, 256, nameof(scope));
        Validate(actor, 255, nameof(actor));
        Validate(name, 128, nameof(name));
        if (correlationId is not null) Validate(correlationId, 256, nameof(correlationId));
        if (operationId == Guid.Empty) throw new ArgumentException("Operation identity must not be empty.", nameof(operationId));
        if (Current is { } current)
        {
            if (current.Scope != scope || current.Actor != actor || operationId is { } id && id != current.Id)
                throw new InvalidOperationException("Nested auditing cannot switch operation identity.");
        }
        else
        {
            Current = new(operationId ?? Guid.CreateVersion7(), scope, actor, name, correlationId);
            _sequence = 0;
        }
        return new Lease(this, ++_depth);
    }

    public int NextSaveSequence() => Current is null
        ? throw new InvalidOperationException("Begin an audit operation before saving audited changes.") : checked(++_sequence);

    private static void Validate(string value, int maximum, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximum) throw new ArgumentOutOfRangeException(name);
        _ = Utf8.GetByteCount(value);
    }
    private sealed class Lease(AuditOperationContext owner, int depth) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            if (owner._depth != depth) throw new InvalidOperationException("Dispose audit scopes in reverse order.");
            _disposed = true;
            if (--owner._depth == 0) owner.Current = null;
        }
    }
}
