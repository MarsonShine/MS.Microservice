using MediatR;
using System;
using System.Threading.Tasks;

namespace MS.Microservice.Domain
{
    public static class DomainEvent
    {
        public static Func<IMediator> Mediator { get; set; }
        public static async Task Raise<T>(T arg) where T : INotification
        {
            var mediator = Mediator.Invoke();
            await mediator.Publish(arg);
        }
    }
}
