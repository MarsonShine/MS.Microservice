using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DDDExample.Domain.Decorator
{
    // 通过装饰者模式封装上下文边界与切换的过程
    public class Buyer
    {
        private readonly User user;

        private List<Order>? orders;
        private List<Payment>? payments;
        public Buyer(User user)
        {
            this.user = user;
        }   

        // 订单上下文
        public void PlaceOrder(Column column) { }
    }
}
