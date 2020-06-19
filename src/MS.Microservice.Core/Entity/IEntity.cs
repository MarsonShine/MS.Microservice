namespace MS.Microservice.Core.Entity
{
    public interface IEntity
    {

    }

    public interface IEntity<TKey> : IEntity
    {
        TKey Id { get; set; }
    }
}
