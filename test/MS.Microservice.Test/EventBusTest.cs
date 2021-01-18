using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Domain;
using MS.Microservice.Test.Etos;
using MS.Microservice.Test.Handles;
using System;
using Xunit;

namespace MS.Microservice.Test
{
    public class EventBusTest
    {
        public ServiceProvider ServiceProvider;
        public IEventBus EventBus;
        public EventBusTest()
        {
            //// Reigister
            //var loggerFactory = LoggerFactory.Create(builder =>
            //{
            //    builder.AddFilter("Microsoft", LogLevel.Warning)
            //    .AddFilter("System", LogLevel.Warning)
            //    .AddFilter("LoggingConsoleApp.Program", LogLevel.Debug)
            //    .AddConsole();
            //});

            ServiceCollection services = new ServiceCollection();
            
            services.AddSingleton<IEventBus, InMemoryEventBus>();
            services.AddLogging();
            
            AddEventBusService(services);

            ServiceProvider = services.BuildServiceProvider();
            EventBus = ServiceProvider.GetService<IEventBus>() ?? 
                throw new ArgumentNullException(nameof(EventBus));
        }

        private void AddEventBusService(ServiceCollection services)
        {
            services.AddTransient<RenamedUserHandle>();
            services.AddTransient<IEventHandle<UserEto>, RenamedUserHandle>();
        }

        [Fact]
        public void SubscribeEvent()
        {
            var user = new UserEto("marsonshine",27,true);
            EventBus.Subscribe<UserEto, RenamedUserHandle>();

            EventBus.PublishAsync(user).ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}
