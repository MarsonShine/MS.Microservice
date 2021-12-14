using MS.Microservice.Core.Dto;
using MS.Microservice.Web.Application.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Application.Queries.Constract
{
    public interface IUserQuery
    {
        Task<PagedResultDto<UserPagedResponse>> GetPagedAsync(string account, int pageIndex = 1, int pageSize = 10,
                                                 CancellationToken cancellationToken = default);


        Task<List<RoleResponse>> GetAllRoleAsync(CancellationToken cancellationToken = default);
    }
}
