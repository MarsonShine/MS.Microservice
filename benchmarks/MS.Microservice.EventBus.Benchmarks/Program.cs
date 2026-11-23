using System.Diagnostics;
using MS.Microservice.EventBus;
using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

const int writes = 4096;
const int lookups = 65_536;
const int workers = 4;
var parallel = new ParallelOptions { MaxDegreeOfParallelism = workers };

Measure("New empty manager", writes, () =>
{
    InMemoryEventBusSubscriptionsManager manager;
    int i = 0;
    do
    {
        manager = new();
        GC.KeepAlive(manager);
    } while (++i < writes);
    return manager;
}, manager => manager.GetEventTypeByName(nameof(OrderPlaced)) is null);

Measure("New manager + first subscription", writes, () =>
{
    InMemoryEventBusSubscriptionsManager manager;
    int i = 0;
    do
    {
        manager = new();
        manager.AddSubscription<OrderPlaced, OrderHandler<int>>();
    } while (++i < writes);
    return manager;
}, manager => manager.GetEventTypeByName(nameof(OrderPlaced)) == typeof(OrderPlaced));

var registry = new InMemoryEventBusSubscriptionsManager();
Measure("New manager + two subscriptions", writes, () =>
{
    InMemoryEventBusSubscriptionsManager manager;
    int i = 0;
    do
    {
        manager = new();
        manager.AddSubscription<OrderPlaced, OrderHandler<int>>();
        manager.AddSubscription<OrderPlaced, OrderHandler<long>>();
    } while (++i < writes);
    return manager;
}, manager =>
{
    manager.RemoveSubscription<OrderPlaced, OrderHandler<int>>();
    return manager.GetEventTypeByName(nameof(OrderPlaced)) == typeof(OrderPlaced);
});

Measure("Generic event key", lookups, () =>
{
    int matches = 0;
    for (int i = 0; i < lookups; i++)
        if (registry.GetEventKey<OrderPlaced>() == nameof(OrderPlaced)) matches++;
    return matches;
}, matches => matches == lookups);

Measure("One handler: add + remove", writes, () =>
{
    for (int i = 0; i < writes; i++)
    {
        registry.AddSubscription<OrderPlaced, OrderHandler<int>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<int>>();
    }
    return registry;
}, manager => manager.GetEventTypeByName(nameof(OrderPlaced)) is null);

registry.AddSubscription<OrderPlaced, OrderHandler<int>>();
Measure("Resident first handler: add + remove second", writes, () =>
{
    for (int i = 0; i < writes; i++)
    {
        registry.AddSubscription<OrderPlaced, OrderHandler<long>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<long>>();
    }
    return registry;
}, manager => manager.GetEventTypeByName(nameof(OrderPlaced)) == typeof(OrderPlaced));
registry.Clear();

Measure("Two handlers: add both + remove both", writes, () =>
{
    for (int i = 0; i < writes; i++)
    {
        registry.AddSubscription<OrderPlaced, OrderHandler<int>>();
        registry.AddSubscription<OrderPlaced, OrderHandler<long>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<int>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<long>>();
    }
    return registry;
}, manager => manager.GetEventTypeByName(nameof(OrderPlaced)) is null);

Measure("Eight handlers: add all + remove all", writes, () =>
{
    for (int i = 0; i < writes; i++)
    {
        registry.AddSubscription<OrderPlaced, OrderHandler<byte>>();
        registry.AddSubscription<OrderPlaced, OrderHandler<short>>();
        registry.AddSubscription<OrderPlaced, OrderHandler<int>>();
        registry.AddSubscription<OrderPlaced, OrderHandler<long>>();
        registry.AddSubscription<OrderPlaced, OrderHandler<float>>();
        registry.AddSubscription<OrderPlaced, OrderHandler<double>>();
        registry.AddSubscription<OrderPlaced, OrderHandler<decimal>>();
        registry.AddSubscription<OrderPlaced, OrderHandler<string>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<byte>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<short>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<int>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<long>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<float>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<double>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<decimal>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<string>>();
    }
    return registry;
}, manager => manager.GetEventTypeByName(nameof(OrderPlaced)) is null);

(Type Type, Action<InMemoryEventBusSubscriptionsManager> Register)[] events =
[
    (typeof(OrderPlaced), bus => bus.AddSubscription<OrderPlaced, AnyOrderHandler>()),
    (typeof(OrderShipped), bus => bus.AddSubscription<OrderShipped, AnyOrderHandler>()),
    (typeof(OrderPaid), bus => bus.AddSubscription<OrderPaid, AnyOrderHandler>()),
    (typeof(OrderCancelled), bus => bus.AddSubscription<OrderCancelled, AnyOrderHandler>()),
    (typeof(OrderInvoiced), bus => bus.AddSubscription<OrderInvoiced, AnyOrderHandler>()),
    (typeof(OrderPacked), bus => bus.AddSubscription<OrderPacked, AnyOrderHandler>()),
    (typeof(OrderDelivered), bus => bus.AddSubscription<OrderDelivered, AnyOrderHandler>()),
    (typeof(OrderRefunded), bus => bus.AddSubscription<OrderRefunded, AnyOrderHandler>())
];
Measure("Eight event names: register all + clear", writes, () =>
{
    for (int i = 0; i < writes; i++)
    {
        foreach (var entry in events) entry.Register(registry);
        registry.Clear();
    }
    return registry;
}, manager => events.All(entry => manager.GetEventTypeByName(entry.Type.Name) is null));

registry.AddSubscription<OrderPlaced, OrderHandler<int>>();
// Use separately constructed strings, as an adapter may obtain the routing key from an incoming message.
string hitKey = new(nameof(OrderPlaced).AsSpan());
string missKey = new(nameof(OrderShipped).AsSpan());
Measure("Lookup: hit", lookups, () => CountMatches(registry, hitKey, typeof(OrderPlaced), lookups), matches => matches == lookups);
Measure("Lookup: miss", lookups, () => CountMatches(registry, missKey, null, lookups), matches => matches == lookups);

Measure("Shared lookup: four readers", workers * lookups, () =>
{
    int hits = 0;
    Parallel.For(0, workers, parallel, _ => Interlocked.Add(ref hits, CountMatches(registry, hitKey, typeof(OrderPlaced), lookups)));
    return hits;
}, hits => hits == workers * lookups);

Measure("Shared registry: three readers + one writer", 3 * lookups + 2 * writes, () =>
{
    int hits = 0;
    Parallel.For(0, workers, parallel, worker =>
    {
        if (worker == 0)
        {
            for (int i = 0; i < writes; i++)
            {
                registry.AddSubscription<OrderShipped, ShippingHandler>();
                registry.RemoveSubscription<OrderShipped, ShippingHandler>();
            }
        }
        else
        {
            Interlocked.Add(ref hits, CountMatches(registry, hitKey, typeof(OrderPlaced), lookups));
        }
    });
    return hits;
}, hits => hits == 3 * lookups && registry.GetEventTypeByName(missKey) is null);

registry.Clear();
foreach (var entry in events) entry.Register(registry);
var routingKeys = events.Select(entry => (Key: new string(entry.Type.Name.AsSpan()), entry.Type)).ToArray();
Measure("Shared lookup: four readers, eight event names", workers * lookups, () =>
{
    int matches = 0;
    Parallel.For(0, workers, parallel, worker =>
    {
        int local = 0;
        for (int i = 0; i < lookups; i++)
        {
            var (key, expected) = routingKeys[(i + worker) % routingKeys.Length];
            if (registry.GetEventTypeByName(key) == expected) local++;
        }
        Interlocked.Add(ref matches, local);
    });
    return matches;
}, matches => matches == workers * lookups);

Action[] churn = [Churn<byte>, Churn<short>, Churn<int>, Churn<long>];
Measure("Shared registry: four writers", workers * writes * 2, () =>
{
    Parallel.For(0, workers, parallel, worker => churn[worker]());
    return registry;
}, manager =>
{
    manager.RemoveSubscription<OrderPlaced, AnyOrderHandler>();
    bool removedLast = manager.GetEventTypeByName(hitKey) is null;
    manager.AddSubscription<OrderPlaced, AnyOrderHandler>();
    return removedLast;
});

void Churn<T>()
{
    for (int i = 0; i < writes; i++)
    {
        registry.AddSubscription<OrderPlaced, OrderHandler<T>>();
        registry.RemoveSubscription<OrderPlaced, OrderHandler<T>>();
    }
}

static int CountMatches(InMemoryEventBusSubscriptionsManager manager, string key, Type? expected, int count)
{
    int hits = 0;
    for (int i = 0; i < count; i++)
        if (manager.GetEventTypeByName(key) == expected) hits++;
    return hits;
}

static void Measure<T>(string name, int operations, Func<T> batch, Func<T, bool> correct)
{
    if (!correct(batch())) throw new InvalidOperationException(name);
    var times = new double[7];
    var allocations = new double[7];
    for (int round = 0; round < times.Length; round++)
    {
        long before = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        var result = batch();
        times[round] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / operations;
        allocations[round] = (double)(GC.GetTotalAllocatedBytes(precise: true) - before) / operations;
        if (!correct(result)) throw new InvalidOperationException(name);
    }
    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine($"{name}: {times[3]:F1} ns/op, {allocations[3]:F2} B/op");
}

sealed class OrderPlaced : IntegrationEvent;
sealed class OrderShipped : IntegrationEvent;
sealed class OrderPaid : IntegrationEvent;
sealed class OrderCancelled : IntegrationEvent;
sealed class OrderInvoiced : IntegrationEvent;
sealed class OrderPacked : IntegrationEvent;
sealed class OrderDelivered : IntegrationEvent;
sealed class OrderRefunded : IntegrationEvent;
sealed class AnyOrderHandler : IIntegrationEventHandler<IntegrationEvent>
{
    public Task Handle(IntegrationEvent @event) => Task.CompletedTask;
}
sealed class OrderHandler<T> : IIntegrationEventHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced @event) => Task.CompletedTask;
}
sealed class ShippingHandler : IIntegrationEventHandler<OrderShipped>
{
    public Task Handle(OrderShipped @event) => Task.CompletedTask;
}
