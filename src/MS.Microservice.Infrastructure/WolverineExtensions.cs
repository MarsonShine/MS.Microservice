using MS.Microservice.Domain;
using MS.Microservice.Infrastructure.DbContext;
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
                    .Entries<Entity>()
                    .Where(x => x.Entity.DomainEvents != null && x.Entity.DomainEvents.Count != 0);

                var domainEvents = domainEntities
                    .SelectMany(x => x.Entity.DomainEvents)
                    .ToList();

                domainEntities.ToList()
                    .ForEach(entity => entity.Entity.ClearDomainEvents());

                foreach (var domainEvent in domainEvents)
                    await messageBus.PublishAsync(domainEvent);
            }
        }
    }
}
