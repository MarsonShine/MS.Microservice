using MS.Microservice.Core.Domain.Repository;

namespace MS.Microservice.Core.Domain
{
    public abstract class DomainService
    {
        protected DomainService()
        {

        }
        IUnitOfWork? UnitOfWork { get; }
    }
}
