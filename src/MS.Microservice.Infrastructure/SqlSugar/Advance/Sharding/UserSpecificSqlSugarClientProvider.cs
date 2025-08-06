using Microsoft.AspNetCore.Http;
using SqlSugar;
using System;

namespace MS.Microservice.Infrastructure.SqlSugar.Advance.Sharding
{
    internal class UserSpecificSqlSugarClientProvider : IUserSpecificSqlSugarClientProvider
    {
        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly IUserHashSplitSqlSugarClientFactory userHashSplitSqlSugarClientFactory;
        private readonly Lazy<ISqlSugarClient> lazyClient;

        public UserSpecificSqlSugarClientProvider(IHttpContextAccessor httpContextAccessor,
            IUserHashSplitSqlSugarClientFactory userHashSplitSqlSugarClientFactory)
        {
            this.httpContextAccessor = httpContextAccessor;
            this.userHashSplitSqlSugarClientFactory = userHashSplitSqlSugarClientFactory;
            lazyClient = new(CreateClient);
        }

        private ISqlSugarClient CreateClient()
        {
            try
            {
                //long userId = httpContextAccessor.GetUserId();
                return userHashSplitSqlSugarClientFactory.GetSqlSugarClient(0);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException("无法获取用户身份信息，请确认用户已登录。", ex);
            }
        }

        public ISqlSugarClient Client => lazyClient.Value;
    }
}
