using MS.Microservice.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Domains.Repositories.Contracts
{
    //可以实现工作单元功能
    public interface IOrderRepository
    {
        Task<int> AddAsync(Order order);
    }
}
