using MediatR;
using MS.Microservice.Domain;
using MS.Microservice.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Database
{
    public static class MediatorExtensions
    {
        public static async Task DispatchDomainEventsAsync(this IMediator mediator, OrderingContext context, CancellationToken cancellationToken = default)
        {
            var domainEntities = context.ChangeTracker
                .Entries<BaseEntity>()
                .Where(x => x.Entity.DomainEvents != null && x.Entity.DomainEvents.Count > 0);

            var domainEvents = domainEntities.SelectMany(x => x.Entity.DomainEvents)
                .ToList();

            domainEntities.ForEach(entity => entity.Entity.ClearDomainEvents());

            foreach (var domainEvent in domainEntities)
            {
                await mediator.Publish(domainEvent, cancellationToken);
            }
        }
    }
}
