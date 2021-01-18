namespace MS.Microservice.Domain
{
    public interface IEntity
    {

    }

    public interface IEntity<TKey> : IEntity
    {
        TKey Id { get;}
    }
}
