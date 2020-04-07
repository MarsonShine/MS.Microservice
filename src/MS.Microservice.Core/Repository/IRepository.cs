using MS.Microservice.Domain;

namespace MS.Microservice.Core.Repository
{
    public interface IRepository
    {
    }

    public interface IRepository<TEntity> where TEntity : BaseEntity
    {

    }
}