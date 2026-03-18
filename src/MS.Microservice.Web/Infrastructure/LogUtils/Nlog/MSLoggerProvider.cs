using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    /// <summary>
    /// 自定义 LoggerProvider，为每个 categoryName 缓存一个 MSLoggerEvent 实例。
    /// </summary>
    public sealed class MSLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, MSLoggerEvent> _loggers = new();
        private readonly IHttpContextAccessor _accessor;
        private bool _disposed;

        public MSLoggerProvider(IHttpContextAccessor accessor)
        {
            _accessor = accessor ?? throw new System.ArgumentNullException(nameof(accessor));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, static (name, acc) => new MSLoggerEvent(name, acc), _accessor);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _loggers.Clear();
                _disposed = true;
            }
        }
    }
}
