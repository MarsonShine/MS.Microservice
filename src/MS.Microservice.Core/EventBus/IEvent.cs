namespace MS.Microservice.Core.EventBus
{
    /// <summary>
    /// Legacy Core EventBus alias. New contracts should implement
    /// <see cref="Messaging.IIntegrationEvent" /> directly.
    /// </summary>
    public interface IEvent : Messaging.IIntegrationEvent { }
}
