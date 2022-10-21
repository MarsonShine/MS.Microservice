using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Text.Json;
using System.Text;
using System;
using System.Threading.Tasks;
using MS.Microservice.Infrastructure.Attributes;
using MS.Microservice.Core.Security.Cryptology;

namespace MS.Microservice.Web.Infrastructure.Mvc.ModelBinder
{
    //https://docs.microsoft.com/en-us/aspnet/core/mvc/advanced/custom-model-binding?view=aspnetcore-5.0
    //https://www.stevejgordon.co.uk/html-encode-string-aspnet-core-model-binding
    public class ApiDecryptModelBinder : IModelBinder
    {
        private readonly ILogger _logger;
        private readonly string _rsaPrivateKey;
        public ApiDecryptModelBinder(ILoggerFactory loggerFactory,
            IConfiguration configuration)
        {
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            _logger = loggerFactory.CreateLogger<ApiDecryptModelBinder>();
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            _rsaPrivateKey = configuration["ApiEncryptOptions:PrivateKey"];
        }

        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            try
            {
                var request = bindingContext.HttpContext.Request;
                if (bindingContext.ActionContext.ActionDescriptor.EndpointMetadata.Any(attribute => attribute.GetType() == typeof(NoEncryptAttribute)))
                {
                    var targetModel = await JsonSerializer.DeserializeAsync(request.Body, bindingContext.ModelMetadata.ModelType, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    });
                    bindingContext.Result = ModelBindingResult.Success(targetModel);
                    return;
                }

                var model = await JsonSerializer.DeserializeAsync<SafeDataWrapper>(request.Body, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });
                if (model == null) throw new ArgumentNullException(nameof(model)); ;

                //解密
                var desKey = CryptologyHelper.RsaCrypt.Decrypt(model.Key!, privateKey: _rsaPrivateKey, Encoding.ASCII);
                var decryptContent = CryptologyHelper.DesCrypt.Decrypt(model.Info!, desKey);
                var activateModel = JsonSerializer.Deserialize(decryptContent, bindingContext.ModelMetadata.ModelType, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                });
                //重新包装
                if (activateModel == null)
                {
                    bindingContext.Result = ModelBindingResult.Failed();
                }
                else
                {
                    bindingContext.Result = ModelBindingResult.Success(activateModel);
                }
            }
            catch (Exception exception)
            {
                bindingContext.ModelState.TryAddModelError(
                    bindingContext.ModelName,
                    exception,
                    bindingContext.ModelMetadata);
            }
        }
    }

    public class SafeDataWrapper
    {
        public string Key { get; set; }
        /// <summary>
        /// 加密字符串
        /// </summary>
        public string Info { get; set; }
    }
}
