using Autofac;
using MassTransit;
using Microsoft.Extensions.Configuration;
using MS.Microservice.Web.Configurations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Web.AutofacModules
{
    public class MassTransitModule : Module
    {
        //private readonly IConfiguration _configuration;
        //public MassTransitModule(IConfiguration configuration)
        //{
        //    _configuration = configuration;
        //}
        protected override void Load(ContainerBuilder builder)
        {
            
            builder.Register(context =>
            {
                var eventBus = context.Resolve<IConfiguration>().Get<EventBus>();
                var busControl = Bus.Factory.CreateUsingRabbitMq(cfg =>
                {
                    cfg.Host(eventBus.Host, "/", cf =>
                    {
                        cf.Username(eventBus.UserName);
                        cf.Password(eventBus.Password);
                    });
                    cfg.ReceiveEndpoint("MS.MassTransit.Inventory.Api", callback => { });
                });
                return busControl;
            })
            .As<IBusControl>()
            .As<IBus>()
            .As<IPublishEndpoint>()
            .SingleInstance();
        }
    }
}
