using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace MS.Microservice.Web.Infrastructure.Mvc.ModelBinder
{
    public class ApiDecryptModelBinderProvider: IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Metadata.IsComplexType && typeof(IApiEncrypt).IsAssignableFrom(context.Metadata.ModelType))
            {
                var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
                var configuration = context.Services.GetRequiredService<IConfiguration>();
                return new ApiDecryptModelBinder(loggerFactory, configuration);
            }

            return default;
        }
    }
}
