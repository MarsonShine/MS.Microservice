using Microsoft.EntityFrameworkCore;
using EfCoreDbContext = Microsoft.EntityFrameworkCore.DbContext;

namespace DDDExample.Infrastructure.DbContext
{
    public class MyDbContext : EfCoreDbContext
    {
        public MyDbContext(DbContextOptions<MyDbContext> options) : base(options)
        {

        }
    }
}
