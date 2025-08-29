using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog.Performance
{
    public class OptimisedLoggingMessage
    {
        private readonly ILogger _logger;
        public OptimisedLoggingMessage(ILogger logger)
        {
            _logger = logger;
        }

        public Action<ILogger, T1, Exception> Define<T1>(LogLevel logLevel, EventId eventId, string format)
        {
            var messageDefine = LoggerMessage.Define<T1>(
                logLevel,
                eventId,
                format);
            return messageDefine;
        }

        public Action<ILogger, T1, T2, Exception> Define<T1, T2>(LogLevel logLevel, EventId eventId, string format)
        {
            var messageDefine = LoggerMessage.Define<T1, T2>(
                logLevel,
                eventId,
                format);
            return messageDefine;
        }

        public Action<ILogger, T1, T2, T3, Exception> Define<T1, T2, T3>(LogLevel logLevel, EventId eventId, string format)
        {
            var messageDefine = LoggerMessage.Define<T1, T2, T3>(
                logLevel,
                eventId,
                format);
            return messageDefine;
        }
        public Action<ILogger, T1, T2, T3, T4, Exception> Define<T1, T2, T3, T4>(LogLevel logLevel, EventId eventId, string format)
        {
            var messageDefine = LoggerMessage.Define<T1, T2, T3, T4>(
                logLevel,
                eventId,
                format);
            return messageDefine;
        }
        public Action<ILogger, T1, T2, T3, T4, T5, Exception> Define<T1, T2, T3, T4, T5>(LogLevel logLevel, EventId eventId, string format)
        {
            var messageDefine = LoggerMessage.Define<T1, T2, T3, T4, T5>(
                logLevel,
                eventId,
                format);
            return messageDefine;
        }
        public Action<ILogger, T1, T2, T3, T4, T5, T6, Exception> Define<T1, T2, T3, T4, T5, T6>(LogLevel logLevel, EventId eventId, string format)
        {
            var messageDefine = LoggerMessage.Define<T1, T2, T3, T4, T5, T6>(
                logLevel,
                eventId,
                format);
            return messageDefine;
        }
    }
}
