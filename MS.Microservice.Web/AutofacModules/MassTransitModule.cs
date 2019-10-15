using Autofac;
using GreenPipes;
using MassTransit;
using Microsoft.Extensions.Configuration;
using MS.Microservice.IntegrateEvent.Contracts;
using MS.Microservice.Web.Configurations;
using MS.Microservice.Web.Subscribers;
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
                var eventBus = context.Resolve<IConfiguration>().GetSection("EventBus").Get<EventBus>();
                var busControl = Bus.Factory.CreateUsingRabbitMq(cfg =>
                {
                    var host = cfg.Host(eventBus.Host, "/", cf =>
                    {
                        cf.Username(eventBus.UserName);
                        cf.Password(eventBus.Password);
                    });

                    cfg.ReceiveEndpoint(host, "MassTransit.Inventory.Api", e =>
                    {
                        // 立即重试
                        //e.UseRetry(retryConfig => retryConfig.Immediate(5));

                        // 时间间隔
                        //e.UseRetry(retryConfig => retryConfig.Interval(10, TimeSpan.FromMilliseconds(2000)));

                        // 按指数重试 重试次数越多 间隔时间越长
                        // 最多重试1000次 最少间隔3秒 最大间隔30秒 间隔3秒
                        // 
                        // 第一次 可能是
                        //e.UseRetry(retryConfig => retryConfig
                        //    .Exponential(1000, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(3)));

                        //e.UseRetry(retryConfig => retryConfig.Immediate(5)); //立即重试
                        //e.LoadFrom(context); // 自动通过反射 加载消费者
                        
                        e.Consumer< IConsumer<IOrderCreatedEvent>>(context); // 指定消费者
                    });

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
