using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.EventBus.Abstractions;
using MS.Microservice.EventBus.Events;

namespace MS.Microservice.EventBus.Tests;

public sealed class InMemoryEventBusTests
{
    [Fact]
    public async Task PublishAwaitsHandlersInOrderAndOwnsOneAsyncScopePerEvent()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<(string Handler, IntegrationEvent Event, ScopeProbe Scope)>();
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddScoped(_ => new ScopeProbe());
        services.AddScoped(sp => new FirstHandler(async message =>
        {
            calls.Add(("first", message, sp.GetRequiredService<ScopeProbe>()));
            entered.TrySetResult();
            await resume.Task;
        }));
        services.AddScoped(sp => new SecondHandler(message =>
        {
            calls.Add(("second", message, sp.GetRequiredService<ScopeProbe>()));
            return Task.CompletedTask;
        }));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var bus = provider.GetRequiredService<IEventBus>();
        Assert.Same(bus, provider.GetRequiredService<IEventbus>());
        bus.Subscribe<Sales.Changed, FirstHandler>();
        bus.Subscribe<Sales.Changed, SecondHandler>();

        IntegrationEvent first = new Sales.Changed();
        var inFlight = bus.PublishAsync(first);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(inFlight.IsCompleted);
            Assert.False(Assert.Single(calls).Scope.Disposed);
            // The snapshot already being consumed must retain its second handler.
            await Task.Run(bus.Unsubscribe<Sales.Changed, SecondHandler>).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            resume.TrySetResult();
        }
        await inFlight;
        Assert.Equal(new[] { "first", "second" }, calls.Select(call => call.Handler));
        Assert.All(calls, call => Assert.Same(first, call.Event));
        Assert.Same(calls[0].Scope, calls[1].Scope);
        Assert.True(calls[0].Scope.Disposed);

        var next = new Sales.Changed();
        await bus.PublishAsync(next);
        Assert.Equal(new[] { "first", "second", "first" }, calls.Select(call => call.Handler));
        Assert.Same(next, calls[2].Event);
        Assert.NotSame(calls[0].Scope, calls[2].Scope);
        Assert.True(calls[2].Scope.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothPublishEntrypointsPropagateTheOriginalFailureAndStopDispatch(bool synchronous)
    {
        var failure = new InvalidOperationException("handler failed");
        var scope = new ScopeProbe();
        var laterCalls = 0;
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddScoped(_ => scope);
        services.AddScoped(sp => new FirstHandler(async _ =>
        {
            sp.GetRequiredService<ScopeProbe>();
            await Task.Yield();
            throw failure;
        }));
        services.AddScoped(_ => new SecondHandler(_ =>
        {
            laterCalls++;
            return Task.CompletedTask;
        }));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var bus = provider.GetRequiredService<IEventBus>();
        bus.Subscribe<Sales.Changed, FirstHandler>();
        bus.Subscribe<Sales.Changed, SecondHandler>();

        var message = new Sales.Changed();
        var error = synchronous
            ? Record.Exception(() => bus.Publish(message))
            : await Record.ExceptionAsync(() => bus.PublishAsync(message));
        Assert.Same(failure, error);
        Assert.Equal(0, laterCalls);
        Assert.True(scope.Disposed);
    }

    [Fact]
    public async Task DirectRegistrationAndBusDispatchShareStateAndRespectConcreteEventTypes()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        services.AddScoped(_ => new MultiHandler(calls));
        services.AddScoped(_ => new FirstHandler(_ =>
        {
            calls.Add("stock");
            return Task.CompletedTask;
        }));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var manager = provider.GetRequiredService<IEventBusSubscriptionsManager>();
        var bus = provider.GetRequiredService<IEventBus>();
        manager.AddSubscription<Sales.Changed, MultiHandler>();
        manager.AddSubscription<Stock.Changed, FirstHandler>();
        bus.Subscribe<Shipped, MultiHandler>();

        Assert.Same(typeof(Sales.Changed), manager.GetEventTypeByName("Changed"));
        await bus.PublishAsync(new Sales.Changed());
        await bus.PublishAsync(new Stock.Changed());
        bus.Publish(new Shipped());
        Assert.Equal(new[] { "sales", "stock", "shipped" }, calls);

        manager.RemoveSubscription<Sales.Changed, MultiHandler>();
        await bus.PublishAsync(new Sales.Changed());
        await bus.PublishAsync(new Stock.Changed());
        manager.Clear();
        await bus.PublishAsync(new Stock.Changed());
        bus.Publish(new Shipped());
        Assert.Equal(new[] { "sales", "stock", "shipped", "stock" }, calls);
    }

    [Fact]
    public async Task ExistingSubscriptionManagerRemainsTheRegistryUsedForDispatch()
    {
        var manager = new InMemoryEventBusSubscriptionsManager();
        manager.AddSubscription<Sales.Changed, FirstHandler>();
        int calls = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IEventBusSubscriptionsManager>(manager);
        services.AddInMemoryEventBus();
        services.AddInMemoryEventBus();
        services.AddScoped(_ => new FirstHandler(_ => { calls++; return Task.CompletedTask; }));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var bus = Assert.Single(provider.GetServices<IEventBus>());

        await bus.PublishAsync(new Sales.Changed());
        Assert.Equal(1, calls);
        manager.Clear();
        await bus.PublishAsync(new Sales.Changed());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SubscribedHandlerMissingFromDiIsReportedToPublisher()
    {
        var services = new ServiceCollection();
        services.AddInMemoryEventBus();
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IEventBus>();
        bus.Subscribe<Sales.Changed, FirstHandler>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new Sales.Changed()));
    }

    private static class Sales { internal sealed class Changed : IntegrationEvent; }
    private static class Stock { internal sealed class Changed : IntegrationEvent; }
    private sealed class Shipped : IntegrationEvent;

    private class FirstHandler(Func<IntegrationEvent, Task> consume) : IIntegrationEventHandler<IntegrationEvent>
    {
        public Task Handle(IntegrationEvent message) => consume(message);
    }

    private sealed class SecondHandler(Func<IntegrationEvent, Task> consume) : FirstHandler(consume);

    private sealed class MultiHandler(List<string> calls) :
        IIntegrationEventHandler<Sales.Changed>, IIntegrationEventHandler<Shipped>
    {
        Task IIntegrationEventHandler<Sales.Changed>.Handle(Sales.Changed message)
        {
            calls.Add("sales");
            return Task.CompletedTask;
        }

        Task IIntegrationEventHandler<Shipped>.Handle(Shipped message)
        {
            calls.Add("shipped");
            return Task.CompletedTask;
        }
    }

    private sealed class ScopeProbe : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            Disposed = true;
        }
    }
}
