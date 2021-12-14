using Dapper;
using MS.Microservice.Core.Dto;
using MS.Microservice.Core.Security;
using MS.Microservice.Web.Application.Models;
using MS.Microservice.Web.Application.Queries.Constract;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Application.Queries
{
    public class UserQuery : IUserQuery
    {
        private readonly string _connectionString = string.Empty;

        public UserQuery(string constr)
        {
            _connectionString = !string.IsNullOrWhiteSpace(constr) ? constr : throw new ArgumentNullException(nameof(constr));
        }

        public async Task<List<RoleResponse>> GetAllRoleAsync(CancellationToken cancellationToken = default)
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var builder = new SqlBuilder();

            var selector = builder.AddTemplate(@"select * from roles /**where**/ /**orderby**/");

            var list = await connection.QueryAsync<dynamic>(selector.RawSql, selector.Parameters);

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
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var builder = new SqlBuilder();

            var selector = builder.AddTemplate(@"select Account,IsDisabled,Telephone,creatorId,updatorId,Email,Name,FzAccount,FzId,RoleIds from users as u left join(select UserId,GROUP_CONCAT(RoleId) as RoleIds from userroles group by UserId) as r1 on u.Id = r1.UserId /**where**/ /**orderby**/");
            var counter = builder.AddTemplate(@"select count(*) from users /**where**/");
            builder.WhereIf(account?.Length > 0, "account = @account", new { account = account });
            var totalCount = await connection.ExecuteScalarAsync<long>(counter.RawSql, counter.Parameters);

            builder.OrderBy("Id asc limit @PageIndex,@PageSize", new { pageIndex = (pageIndex - 1) * pageSize, pageSize });
            var list = await connection.QueryAsync<dynamic>(selector.RawSql, selector.Parameters);

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
