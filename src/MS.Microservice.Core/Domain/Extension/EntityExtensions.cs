using MS.Microservice.Core.Domain.Entity;

namespace MS.Microservice.Core.Domain.Extension
{
    public static partial class EntityExtensions
    {
        extension(IEntity entity)
        {
            public bool IsNull() => entity == null;
        }
    }
}
