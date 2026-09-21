namespace MS.Microservice.Lab.AotExamples.Legacy.EntityKeys
{
    public interface IEntity<TId> : IEntity
    {
        TId Id { get; set; }
    }

    public interface IEntity
    {
        object[] GetKeys();
    }
}
