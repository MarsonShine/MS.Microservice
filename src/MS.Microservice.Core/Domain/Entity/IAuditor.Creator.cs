namespace MS.Microservice.Core.Domain.Entity
{
    public interface ICreator<TId>
    {
        TId CreatorId { get; }
    }
}
