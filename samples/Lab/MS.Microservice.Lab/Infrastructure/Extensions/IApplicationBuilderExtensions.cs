using Microsoft.AspNetCore.Builder;

namespace MS.Microservice.Lab.Infrastructure.Extensions
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
