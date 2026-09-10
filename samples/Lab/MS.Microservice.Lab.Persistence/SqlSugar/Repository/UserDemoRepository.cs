using MS.Microservice.Domain.SqlSugar.Repository;
using MS.Microservice.Persistence.SqlSugar.DbContext;

namespace MS.Microservice.Persistence.SqlSugar.Repository
{
    public class UserDemoRepository(UserDemoDbContext sqlSugarClient) : SqlSugarDbContext<UserDemo>(() => sqlSugarClient), IUserDemoRepository
    {
    }
}
