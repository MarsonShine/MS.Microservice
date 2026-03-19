using Autofac;

namespace MS.Microservice.Web.AutofacModules.Extensions
{
    /// <summary>
    /// MediatR registration has been replaced by Wolverine.
    /// Wolverine auto-discovers handlers, so this method is now mostly empty.
    /// Keep this method for backward compatibility or remove if not needed.
    /// </summary>
    public static partial class AutofacModuleExtensions
    {
        extension(ContainerBuilder builder)
        {
            public void RegisterPlatformAutofacModule(IConfiguration configuration)
            {
                builder
                    .RegisterModule<CommonInfrastructureModule>()
                    .RegisterModule<DomainServiceModule>()
                    .RegisterModule(new AppServiceModule(configuration.GetConnectionString("ActivationReaderConnection")!))
                    ;
            }
        }
    }
}
