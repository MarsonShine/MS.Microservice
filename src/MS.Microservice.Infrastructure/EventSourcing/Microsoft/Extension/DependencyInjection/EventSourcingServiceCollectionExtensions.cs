using Microsoft.EntityFrameworkCore;
using MS.Microservice.Domain.Aggregates.OrderAggregate;
using MS.Microservice.Domain.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing;
using MS.Microservice.Infrastructure.EventSourcing.Orders;
using MS.Microservice.Infrastructure.EventSourcing.Repository;
using MS.Microservice.Infrastructure.EventSourcing.Serialization;
using System;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class EventSourcingServiceCollectionExtensions
    {
        public static IServiceCollection AddPostgresEventSourcing(
            this IServiceCollection services,
            string connectionString,
            Action<EventTypeRegistry>? configureRegistry = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            services.AddDbContext<EventStoreDbContext>(options =>
                options.UseNpgsql(connectionString));

            var eventTypeRegistry = new EventTypeRegistry()
                .Register<OrderCreated>()
                .Register<OrderItemAdded>()
                .Register<OrderItemRemoved>()
                .Register<OrderConfirmed>()
                .Register<OrderCancelled>();

            configureRegistry?.Invoke(eventTypeRegistry);

            services.AddSingleton(eventTypeRegistry);
            services.AddSingleton<SystemTextJsonEventSerializer>();
            services.AddScoped<IEventStore, PostgresEventStore>();
            services.AddScoped<ISnapshotStore, PostgresSnapshotStore>();
            services.AddScoped<IProjectionCheckpointStore, PostgresProjectionCheckpointStore>();
            services.AddScoped<OrderCommandService>();
            services.AddScoped<OrderReadModelProjector>();
            return services;
        }
    }
}
