using MS.Microservice.Core.Data;
using MS.Microservice.Domain;
using MS.Microservice.Repostitory.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Database.Repository
{
    public class OrderRepository : IOrderRepository
    {
        private readonly OrderingContext _orderingContext;
        public OrderRepository(OrderingContext orderingContext)
        {
            _orderingContext = orderingContext;
        }

        public IUnitOfWork UnitOfWork => _orderingContext;

        public async Task<int> AddAsync(Order order)
        {
            var orderAdded = await _orderingContext.AddAsync(order);
            return orderAdded.Entity.Id;
        }

        public async Task<bool> DeleteAsync(Order order)
        {
            order.Remove();
            return await Task.FromResult(order.IsDelete == true);
        }

        public async Task<bool> DeleteAsync(int ordid)
        {
            var order = await _orderingContext.Orders.FindAsync(ordid);
            if (order == null) return true;

            order.Delete();
            // TODO UnitOfWork 在 SaveEntity 之前状态可能不会变
            return order.IsDelete;
        }
    }
}
