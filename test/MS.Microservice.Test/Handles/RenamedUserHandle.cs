using Microsoft.Extensions.Logging;
using MS.Microservice.Core.EventEntity;
using MS.Microservice.Test.Etos;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MS.Microservice.Test.Handles
{
    public class RenamedUserHandle : IEventHandle<UserEto>
    {
        private readonly ILogger<RenamedUserHandle> _logger;
        public RenamedUserHandle(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<RenamedUserHandle>();
        }
        public async Task Handle(UserEto @event)
        {
            _logger.LogInformation("消费事件 RenameUserHandle 事件");
            var content = JsonSerializer.Serialize(@event);
            _logger.LogInformation($"事件内容：{content}");
            await Task.CompletedTask;
        }
    }
}
