using DDDExample.Domain.SwitchContext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DDDExample.Domain.Repository
{
    public interface IUserRepositoryV2
    {
        User FindUserById(long id);
        ISubscriptionContext InSubscriptionContext();
        ISocialContext InSocialContext();
        IOrderContext InOrderContext();
    }
}
