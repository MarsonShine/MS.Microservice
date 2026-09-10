using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace MS.Microservice.Lab.Infrastructure.Mvc.ModelBinder.Extension
{
    public static partial class MvcOptionsExtensions
    {
        extension(MvcOptions opts)
        {
            public void UseApiDecryptModelBinding(IConfiguration configuration)
            {
                var enabled = configuration.GetSection("ApiEncryptOptions:IsEnabled").Get<bool>();
                if (enabled)
                    opts.ModelBinderProviders.Insert(0, new ApiDecryptModelBinderProvider());
            }
        }
    }
}
