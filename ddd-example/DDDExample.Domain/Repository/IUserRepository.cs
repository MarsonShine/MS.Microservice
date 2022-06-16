using DDDExample.Domain.Decorator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DDDExample.Domain.Repository
{
    // 通过仓储来实现上下文切换，避免将所有的上下文信息聚焦与一个实体
    public interface IUserRepository
    {
        User FindById(long id);
        Buyer AsBuyer(User user);
        Reader AsReader(User user);
        Contact AsContact(User user);
    }
}
