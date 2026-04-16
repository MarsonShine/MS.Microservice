using Dapper;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Security;
using MS.Microservice.Core.Functional;
using MS.Microservice.Web.Application.Models;
using MS.Microservice.Web.Application.Queries.Constract;
using MS.Microservice.Web.Infrastructure.Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Application.Queries
{
    public class UserQuery : IUserQuery
    {
        private readonly ConnectionString _connectionString;

        public UserQuery(ConnectionString connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<List<RoleResponse>> GetAllRoleAsync(CancellationToken cancellationToken = default)
        {
            var builder = new SqlBuilder();
            var selector = builder.AddTemplate(@"select * from roles /**where**/ /**orderby**/");
            var queryAsync = _connectionString.QueryAsync<dynamic>();
            var queryRoles = queryAsync(selector.RawSql);
            var queryRolesWithParameters = queryRoles((object?)selector.Parameters);
            var list = await queryRolesWithParameters(cancellationToken);

            var logs = list.Select(p => new RoleResponse
            {
                RoleId = p.Id,
                Name = p.Name,
                Description = p.Description,

            }).ToList();

            return logs.AsList();
        }

        public async Task<PagedResultDto<UserPagedResponse>> GetPagedAsync(string account, int pageIndex = 1, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            var builder = new SqlBuilder();
            var selector = builder.AddTemplate(@"select Account,IsDisabled,Telephone,creatorId,updatorId,Email,Name,FzAccount,FzId,RoleIds from users as u left join(select UserId,GROUP_CONCAT(RoleId) as RoleIds from userroles group by UserId) as r1 on u.Id = r1.UserId /**where**/ /**orderby**/");
            var counter = builder.AddTemplate(@"select count(*) from users /**where**/");
            builder.WhereIf(account?.Length > 0, "account = @account", new { account = account });
            var executeScalarAsync = _connectionString.ExecuteScalarAsync<long>();
            var countUsers = executeScalarAsync(counter.RawSql);
            var countUsersWithParameters = countUsers((object?)counter.Parameters);
            var totalCount = await countUsersWithParameters(cancellationToken);

            builder.OrderBy("Id asc limit @PageIndex,@PageSize", new { pageIndex = (pageIndex - 1) * pageSize, pageSize });
            var queryAsync = _connectionString.QueryAsync<dynamic>();
            var queryUsers = queryAsync(selector.RawSql);
            var queryUsersWithParameters = queryUsers((object?)selector.Parameters);
            var list = await queryUsersWithParameters(cancellationToken);

            var logs = list.Select(p => new UserPagedResponse
            {
                Account = p.Account,
                IsDisabled = p.IsDisabled,
                Telephone = SecretField.Phone(p.Telephone),
                CreatorId = p.creatorId,
                UpdatorId = p.updatorId,
                Email = SecretField.HideEmailDetails(p.Email),
                Name = p.Name,
                FzAccount = p.FzAccount,
                FzId = p.FzId,
                Role = p.RoleIds.Split(","),
            }).ToList();

            return new PagedResultDto<UserPagedResponse>(totalCount: totalCount, logs.AsList());
        }
    }
}
