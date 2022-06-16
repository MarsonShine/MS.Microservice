using DDDExample.Domain.SwitchContext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DDDExample.Infrastructure.Repository
{
    // 通过这种关联的思想，这样就能减少单个聚合融合不同上下文的知识过载问题。
    public class UserRepositoryV2
    {
        private readonly ISubscriptionContext subscriptionContext;
        private readonly IOrderContext orderContext;
        private readonly ISocialContext socialContext;
        public UserRepositoryV2(ISubscriptionContext subscriptionContext,IOrderContext orderContext, ISocialContext socialContext)
        {
            this.subscriptionContext = subscriptionContext;
            this.orderContext = orderContext;
            this.socialContext = socialContext;
        }
    }
}
