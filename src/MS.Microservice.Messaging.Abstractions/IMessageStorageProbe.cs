namespace MS.Microservice.Messaging;

/// <summary>Checks the selected provider's required message storage without creating or changing its schema.</summary>
public interface IMessageStorageProbe
{
    Task CheckAsync(CancellationToken cancellationToken);
}
