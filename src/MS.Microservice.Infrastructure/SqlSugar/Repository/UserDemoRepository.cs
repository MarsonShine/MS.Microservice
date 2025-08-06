using MS.Microservice.Domain.SqlSugar.Repository;
using MS.Microservice.Infrastructure.DbContext;
using MS.Microservice.Infrastructure.DbContext.SqlSugar;

namespace MS.Microservice.Infrastructure.SqlSugar.Repository
{
    public class UserDemoRepository(UserDemoDbContext sqlSugarClient) : SqlSugarDbContext<UserDemo>(() => sqlSugarClient), IUserDemoRepository
    {
    }
}
