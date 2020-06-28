using MS.Microservice.Domain;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Repostitory.Contracts
{
    public interface IOrderRepository
    {
        Task<int> AddAsync(Order order);
    }
}
