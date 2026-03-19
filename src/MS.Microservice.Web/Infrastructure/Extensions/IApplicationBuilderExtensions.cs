using Microsoft.AspNetCore.Builder;

namespace MS.Microservice.Web.Infrastructure.Extensions
{
    public static partial class IApplicationBuilderExtensions
    {
        extension(IApplicationBuilder builder)
        {
            public void UseHealthCheck()
            {
            }
        }
    }
}
