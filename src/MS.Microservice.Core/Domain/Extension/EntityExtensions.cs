using MS.Microservice.Core.Domain.Entity;

namespace MS.Microservice.Core.Domain.Extension
{
    public static class EntityExtensions
    {
        public static bool IsNull(this IEntity entity) => entity == null;
    }
}
