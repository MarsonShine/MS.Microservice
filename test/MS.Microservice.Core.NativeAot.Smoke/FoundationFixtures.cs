using System.Linq.Expressions;
using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Core.EventBus;
using MS.Microservice.Core.Specification;

namespace MS.Microservice.Core.NativeAot.Smoke;

internal class KeyedRow<T>(T id) : IEntity<T> { public T Id { get; set; } = id; }
internal sealed class DerivedRow(int id) : KeyedRow<int>(id);
internal sealed class UnrelatedRow(int id) : IEntity<int> { public int Id { get; set; } = id; }
internal readonly record struct CompositeKey(Guid Tenant, int Number);
internal readonly record struct ConstructedKey
{
    public int Value { get; }
    public ConstructedKey() => Value = 99;
}
internal sealed record ReferenceEvent(string Value) : IEvent;
internal readonly record struct ValueEvent(int Value) : IEvent;
internal sealed class RecordingEventHandler<T>(Func<T, Task> handle) : IEventHandler<T> where T : IEvent
{
    public Task Handle(T evt) => handle(evt);
}

internal sealed class SpecRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public SpecChild Child { get; set; } = new();
    public List<SpecChild> Children { get; set; } = [];
    public ICollection<SpecChild> Members { get; set; } = [];
}
internal sealed class SpecChild;
internal sealed class ConsumerSpecification : Specification<SpecRow, string>
{
    public ConsumerSpecification(bool populated)
    {
        if (!populated) return;
        Where(row => row.Id > 0);
        Where(other => other.Id < 10);
        OrWhere(last => last.Name == "special");
        Include(row => row.Child);
        Include(row => row.Id);
        IncludeList(row => row.Children);
        IncludeCollection(row => row.Members);
        OrderBy(row => row.Id);
        ThenByDescending(row => row.Name);
        ApplyPaging(0, 10);
        IgnoreGlobalQueryFilters();
        Select(row => row.Name);
    }
}

internal sealed class IncludeRecorder<T> : IIncludeExpressionVisitor<List<string>, T>
{
    public List<string> VisitSingleInclude<P>(List<string> query, Expression<Func<T, P>> expression)
    { query.Add($"single:{typeof(P).Name}:{Member(expression)}"); return query; }
    public List<string> VisitCollectionInclude<P>(List<string> query, Expression<Func<T, List<P>>> expression)
    { query.Add($"list:{typeof(P).Name}:{Member(expression)}"); return query; }
    public List<string> VisitICollectionInclude<P>(List<string> query, Expression<Func<T, ICollection<P>>> expression)
    { query.Add($"collection:{typeof(P).Name}:{Member(expression)}"); return query; }
    private static string Member(LambdaExpression expression) => ((MemberExpression)expression.Body).Member.Name;
}

internal sealed class ParameterAudit(ParameterExpression expected) : ExpressionVisitor
{
    public int Count { get; private set; }
    protected override Expression VisitParameter(ParameterExpression node)
    {
        FoundationScenarios.Check(ReferenceEquals(node, expected), "Combined expression contains an unbound parameter.");
        Count++;
        return node;
    }
}
