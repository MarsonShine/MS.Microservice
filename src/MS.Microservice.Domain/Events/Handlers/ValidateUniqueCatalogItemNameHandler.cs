using MediatR;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.Events.Handlers
{
    public class ValidateUniqueCatalogItemNameHandler : INotificationHandler<UpdatingNameEvent>
    {
        //private readonly IAsyncRepository<CatalogItem> _asyncRepository;
        public Task Handle(UpdatingNameEvent notification, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
