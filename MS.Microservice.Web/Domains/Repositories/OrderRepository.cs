namespace MS.Microservice.Web.Domains.Repositories
{
    using MS.Microservice.Domain;
    using MS.Microservice.Web.Domains.Repositories.Contracts;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    public class OrderRepository : IOrderRepository
    {
        public static HashSet<Order> Orders = new HashSet<Order>();
        public async Task<int> AddAsync(Order order)
        {
            if (Orders.Any(p => p.OrderNumber == order.OrderNumber))
                return 0;
            Orders.Add(order);
            await Task.CompletedTask;
            order.SetID(1);
            return order.Id;
        }
    }
}
