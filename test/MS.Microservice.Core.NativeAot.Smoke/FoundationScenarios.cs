using System.Linq.Expressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Core.EventBus;
using MS.Microservice.Core.FeatureManager;
using MS.Microservice.Core.Reflection;
using MS.Microservice.Core.Specification;

namespace MS.Microservice.Core.NativeAot.Smoke;

internal static class FoundationScenarios
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("type-defaults-names", TypeDefaults), ("entity-key-contracts", EntityKeys),
        ("event-static-dispatch", EventDispatch), ("feature-di-configuration", FeatureConfiguration),
        ("expression-composition", ExpressionComposition), ("include-generic-dispatch", IncludeDispatch),
        ("reactive-partition-merge", ReactiveScenarios.PartitionAndMerge),
        ("reactive-async-safety", ReactiveScenarios.AsyncSafety),
        ("reactive-hot-pairs-trace", ReactiveScenarios.HotPairsAndTrace)
    ];

    internal static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static T Throws<T>(Action operation) where T : Exception
    {
        try { operation(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static Task TypeDefaults()
    {
        Check(TypeHelper.GetDefaultValue<ConstructedKey>().Value == 0 && new ConstructedKey().Value == 99,
            "default(T) must not execute a value type constructor.");
        Check(TypeHelper.IsDefaultValue(0) && !TypeHelper.IsDefaultValue(1), "Integer default changed.");
        Check(TypeHelper.IsDefaultValue<int?>(null) && !TypeHelper.IsDefaultValue<int?>(0), "Nullable default changed.");
        Check(TypeHelper.IsDefaultValue<string?>(null) && !TypeHelper.IsDefaultValue(""), "Reference default changed.");
        Check(TypeHelper.GetGenericTypeName(typeof(Dictionary<string, int>)) == "Dictionary<String,Int32>", "Generic type name changed.");
        Check(TypeHelper.GetGenericTypeName(new List<int>()) == "List<Int32>", "Object type name changed.");
        Check(TypeHelper.GetGenericTypeName(typeof(int)) == "Int32", "Non-generic type name changed.");
        Check(TypeHelper.GetFullMethodName<int>("Parse") == "System.Int32.Parse", "Method display name changed.");
        return Task.CompletedTask;
    }

    private static Task EntityKeys()
    {
        foreach (var id in new[] { int.MinValue, -1, 0, 1, int.MaxValue })
        {
            var row = new KeyedRow<int>(id);
            Check(EntityHelper.HasDefaultId(row) == (id <= 0), "Integer temporary-key rule changed.");
            Check(EntityHelper.EntityEquals(row, new KeyedRow<int>(id)) == (id > 0), "Integer identity equality changed.");
            Check(EntityHelper.EntityEquals(row, row), "Reference identity must hold even for temporary keys.");
        }
        Check(EntityHelper.HasDefaultKeys(new KeyedRow<long>(long.MinValue)), "Long temporary key was lost.");
        Check(!EntityHelper.HasDefaultKeys(new KeyedRow<long>(long.MaxValue)), "Valid long key was treated as default.");
        foreach (int? id in new int?[] { null, 0, -1, 7 })
            Check(EntityHelper.HasDefaultId(new KeyedRow<int?>(id)) == (id is null), "Nullable key must use its declared default.");
        Check(EntityHelper.HasDefaultId(new KeyedRow<string?>(null)) && !EntityHelper.HasDefaultId(new KeyedRow<string>("")), "Null and empty keys were conflated.");
        var key = new CompositeKey(Guid.Empty, 7);
        Check(!EntityHelper.HasDefaultId(new KeyedRow<CompositeKey>(key)), "Partially default composite key must remain assigned.");
        Check(EntityHelper.HasDefaultId(new KeyedRow<CompositeKey>(default)), "Default composite key was not recognized.");
        Check(EntityHelper.EntityEquals(new KeyedRow<CompositeKey>(key), new KeyedRow<CompositeKey>(key)), "Composite equality changed.");
        Check(!EntityHelper.EntityEquals(new KeyedRow<CompositeKey>(key), new KeyedRow<CompositeKey>(key with { Number = 8 })), "Unequal composite keys matched.");
        var parent = new KeyedRow<int>(7);
        Check(EntityHelper.EntityEquals(parent, new DerivedRow(7)) && EntityHelper.EntityEquals(new DerivedRow(7), parent), "Assignable entity types must compare symmetrically.");
        Check(!EntityHelper.EntityEquals(parent, new UnrelatedRow(7)) && !EntityHelper.EntityEquals<int>(null, parent), "Unrelated or null entities matched.");
        Throws<ArgumentNullException>(() => EntityHelper.HasDefaultId<int>(null!));
        return Task.CompletedTask;
    }

    private static async Task EventDispatch()
    {
        var bus = new EventBusManager();
        var seen = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new RecordingEventHandler<ReferenceEvent>(async e => { seen.Add(e.Value); await release.Task; });
        var second = new RecordingEventHandler<ReferenceEvent>(e => { seen.Add("second"); return Task.CompletedTask; });
        var valueHandler = new RecordingEventHandler<ValueEvent>(e => { seen.Add(e.Value.ToString()); return Task.CompletedTask; });
        bus.Subscribe<ReferenceEvent, RecordingEventHandler<ReferenceEvent>>(first);
        bus.Subscribe<ReferenceEvent, RecordingEventHandler<ReferenceEvent>>(second);
        bus.Subscribe<ValueEvent, RecordingEventHandler<ValueEvent>>(valueHandler);
        var pending = bus.PublishAsync(new ReferenceEvent("first"));
        Check(!pending.IsCompleted && seen.SequenceEqual(["first"]), "Event handlers must be awaited in registration order.");
        release.SetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        bus.UnSubscribe<ReferenceEvent, RecordingEventHandler<ReferenceEvent>>(first);
        await bus.PublishAsync(new ReferenceEvent("removed"));
        await bus.PublishAsync(new ValueEvent(5));
        Check(seen.SequenceEqual(["first", "second", "second", "5"]), "Type isolation or unsubscribe changed.");
        Check(bus.GetEventHandlers<ReferenceEvent>().Count == 1, "Handler registry count changed.");
        var failure = new InvalidOperationException("handler failure");
        var broken = new EventBusManager();
        broken.Subscribe<ValueEvent, RecordingEventHandler<ValueEvent>>(
            new RecordingEventHandler<ValueEvent>(_ => Task.FromException(failure)));
        try { await broken.PublishAsync(new ValueEvent(0)); }
        catch (InvalidOperationException caught) when (ReferenceEquals(caught, failure)) { return; }
        throw new InvalidOperationException("Handler exception was not preserved.");
    }

    private static Task FeatureConfiguration()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["FeatureToggles:flag"] = " true " }).Build();
        var services = new ServiceCollection();
        // Hosts register IConfiguration; AddFeatureToggle registers only the feature services.
        services.AddSingleton(configuration);
        services.AddFeatureToggle(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var manager = provider.GetRequiredService<FeatureToggleManager>();
        Check(ReferenceEquals(manager, provider.GetRequiredService<FeatureToggleManager>()), "Feature manager lost singleton lifetime.");
        Check(manager.IsEnabled("flag") && !manager.IsEnabled("missing"), "Feature true/missing behavior changed.");
        foreach (var value in new[] { "False", " false " })
        { configuration["FeatureToggles:flag"] = value; Check(!manager.IsEnabled("flag"), "Updated false value was ignored."); }
        foreach (var value in new[] { "", "yes", "1" })
        {
            configuration["FeatureToggles:flag"] = value;
            var error = Throws<InvalidOperationException>(() => manager.IsEnabled("flag"));
            Check(error.InnerException is FormatException && error.Message.Contains("FeatureToggles:flag"), "Configuration error lost path or cause.");
        }
        return Task.CompletedTask;
    }

    private static Task ExpressionComposition()
    {
        var empty = new ConsumerSpecification(false);
        Check(empty.Criteria is null && empty.Includes.Count == 0 && !empty.IsPagingEnabled, "Empty specification defaults changed.");
        var spec = new ConsumerSpecification(true);
        var body = (BinaryExpression)spec.Criteria!.Body;
        Check(body.NodeType == ExpressionType.OrElse && body.Left.NodeType == ExpressionType.AndAlso, "Specification boolean grouping changed.");
        var audit = new ParameterAudit(spec.Criteria.Parameters.Single());
        audit.Visit(body);
        Check(audit.Count == 3, "Specification lost a predicate operand.");
        Check(spec.Skip == 0 && spec.Take == 10 && spec.IgnoreQueryFilters, "Specification options changed.");
        Check(spec.OrderExpressions.Select(x => x.OrderType).SequenceEqual([OrderType.OrderBy, OrderType.ThenByDescending]), "Ordering sequence changed.");
        Check(spec.Selector!.Body is MemberExpression { Member.Name: "Name" }, "Projection was lost.");
        var predicate = PredicateBuilder.New<int>(false);
        Check(predicate.Body is ConstantExpression { Value: false }, "Predicate default changed.");
        predicate.And(x => x > 0);
        predicate.Or(y => y == -1);
        Expression<Func<int, bool>> combined = predicate;
        var predicateAudit = new ParameterAudit(combined.Parameters.Single());
        predicateAudit.Visit(combined.Body);
        Check(combined.Body.NodeType == ExpressionType.OrElse && predicateAudit.Count == 2, "Predicate starter failed to rebind parameters.");
        return Task.CompletedTask;
    }

    private static Task IncludeDispatch()
    {
        var spec = new ConsumerSpecification(true);
        var visits = new List<string>();
        var recorder = new IncludeRecorder<SpecRow>();
        foreach (var include in spec.Includes)
            Check(ReferenceEquals(visits, include.Accept(recorder, visits)), "Include visitor did not return the supplied query.");
        Check(visits.SequenceEqual(["single:SpecChild:Child", "single:Int32:Id", "list:SpecChild:Children", "collection:SpecChild:Members"]),
            "Include generic dispatch or expression metadata changed.");
        var wrong = new IncludeRecorder<int>();
        foreach (var include in spec.Includes)
            Throws<InvalidOperationException>(() => include.Accept(wrong, visits));
        Check(visits.Count == 4, "Mismatched entity type reached the visitor.");
        return Task.CompletedTask;
    }
}
