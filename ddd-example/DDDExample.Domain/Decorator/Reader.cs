using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DDDExample.Domain.Decorator
{
    public class Reader
    {
        private readonly User user;
        private List<Subscription>? subscriptions;

        public Reader(User user)
        {
            this.user = user;
        }

        // 订阅上下文
        public bool CanView(Content content)
        {
            return true;
        }
    }
}
