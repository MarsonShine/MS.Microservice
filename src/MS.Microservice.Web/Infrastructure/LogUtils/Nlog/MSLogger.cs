using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MS.Microservice.Web.Infrastructure.LogUtils.Nlog
{
    public class MSLoggerEvent : ILogger, IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly string? _format;
        private readonly object[]? _parameters;
        private IReadOnlyList<KeyValuePair<string, object>> _logValues = Array.Empty<KeyValuePair<string, object>>();
        private List<KeyValuePair<string, object>>? _extraProperties;

        private readonly IHttpContextAccessor? _accessor;
        private readonly string? _categoryName;

        // Provider mode
        public MSLoggerEvent(string categoryName, IHttpContextAccessor accessor)
        {
            _categoryName = categoryName;
            _accessor = accessor;
        }

        public MSLoggerEvent(string format, params object[] values)
        {
            _format = format;
            _parameters = values;
        }

        public MSLoggerEvent WithProperty(string name, object value)
        {
            var properties = _extraProperties ??= new List<KeyValuePair<string, object>>();
            if (!properties.Any(p => p.Key == name))
                properties.Add(new KeyValuePair<string, object>(name, value));
            return this;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            if (MessagePropertyCount == 0)
            {
                if (ExtraPropertyCount > 0)
                    return (_extraProperties ?? new()).GetEnumerator();
                else
                    return Array.Empty<KeyValuePair<string, object>>().AsEnumerable().GetEnumerator();
            }
            else
            {
                if (ExtraPropertyCount > 0)
                    return (_extraProperties ?? new()).Concat(LogValues).GetEnumerator();
                else
                    return LogValues.GetEnumerator();
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public KeyValuePair<string, object> this[int index]
        {
            get
            {
                int extraCount = ExtraPropertyCount;
                if (index < extraCount)
                {
                    return _extraProperties![index];
                }
                else
                {
                    return LogValues[index - extraCount];
                }
            }
        }

        public int Count => MessagePropertyCount + ExtraPropertyCount;

        public override string ToString() => LogValues.ToString() ?? string.Empty;

        private IReadOnlyList<KeyValuePair<string, object>> LogValues
        {
            get
            {
                if (_logValues == null || _logValues.Count == 0)
                    this.LogDebug(_format ?? string.Empty, _parameters ?? Array.Empty<object>());
                return _logValues ?? Array.Empty<KeyValuePair<string, object>>();
            }
        }

        private int ExtraPropertyCount => _extraProperties?.Count ?? 0;

        private int MessagePropertyCount
        {
            get
            {
                if (LogValues.Count > 1 && !string.IsNullOrEmpty(LogValues[0].Key) && !char.IsDigit(LogValues[0].Key[0]))
                    return LogValues.Count;
                else
                    return 0;
            }
        }

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _logValues = state as IReadOnlyList<KeyValuePair<string, object>> ?? Array.Empty<KeyValuePair<string, object>>();
        }

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        IDisposable ILogger.BeginScope<TState>(TState state) => NullDisposable.Instance;

        public static Func<MSLoggerEvent, Exception?, string?> Formatter { get; } = (l, e) => l.LogValues.ToString();

        private class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new NullDisposable();
            public void Dispose() { }
        }
    }

    public static class CustomLoggerExtensions
    {
        public static ILoggerFactory AddMSLogger(this ILoggerFactory factory, IHttpContextAccessor accessor)
        {
            factory.AddProvider(new MSLoggerProvider(accessor));
            return factory;
        }
    }
}
