using MS.Microservice.Domain;
using MS.Microservice.Persistence.EFCore.DbContext;
using Wolverine;

namespace MS.Microservice.Infrastructure
{
    public static partial class WolverineExtensions
    {
        extension(IMessageBus messageBus)
        {
            public async Task DispatchDomainEventsAsync(ActivationDbContext ctx)
            {
                var domainEntities = ctx.ChangeTracker
                    .Entries()
                    .Select(entry => entry.Entity)
                    .OfType<IHasDomainEvents>()
                    .Where(entity => entity.DomainEvents.Count != 0)
                    .ToList();

                var domainEvents = domainEntities
                    .SelectMany(entity => entity.DomainEvents)
                    .ToList();

                foreach (var domainEvent in domainEvents)
                    await messageBus.PublishAsync(domainEvent);

                domainEntities.ForEach(entity => entity.ClearDomainEvents());
            }
        }
    }
}
