using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MS.Microservice.Web.Infrastructure.LogUtils.Nlog;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MS.Microservice.Core.Tests.Web.Infrastructure.LogUtils.Nlog
{
    public sealed class MSLoggerMiddlewareTests : IDisposable
    {
        [Fact]
        public async Task InvokeAsync_ShouldKeepRenderedDurationConsistentWithLoggedMessage()
        {
            using var testContext = CreateLoggingContext("dur=${RequestDuration}|msg=${message}");

            var httpContext = testContext.CreateHttpContext(HttpMethods.Get, "/logs/duration");

            var middleware = CreateMiddleware(
                testContext,
                next: context =>
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    testContext.TimeProvider.Advance(TimeSpan.FromMilliseconds(1234));
                    return Task.CompletedTask;
                });

            await middleware.InvokeAsync(httpContext);

            testContext.Flush();

            testContext.Logs.Should().ContainSingle();
            var renderedLog = testContext.Logs.Single();
            renderedLog.Should().Contain("dur=1234ms");
            renderedLog.Should().Contain("msg=HTTP GET /logs/duration -> 204 in 1234ms");
        }

        [Fact]
        public async Task InvokeAsync_ShouldRenderRequestHeadersIntoNLogLayout()
        {
            using var testContext = CreateLoggingContext("rid=${requestId}|pid=${platformId}|uflag=${userflag}|dur=${RequestDuration}|msg=${message}");

            var httpContext = testContext.CreateHttpContext(HttpMethods.Post, "/logs/headers");
            httpContext.Request.Headers["requestId"] = "req-001";
            httpContext.Request.Headers["platformId"] = "platform-a";
            httpContext.Request.Headers["userflag"] = "u-flag";

            var middleware = CreateMiddleware(
                testContext,
                next: context =>
                {
                    context.Response.StatusCode = StatusCodes.Status202Accepted;
                    testContext.TimeProvider.Advance(TimeSpan.FromMilliseconds(85));
                    return Task.CompletedTask;
                });

            await middleware.InvokeAsync(httpContext);

            testContext.Flush();

            testContext.Logs.Should().ContainSingle();
            var renderedLog = testContext.Logs.Single();
            renderedLog.Should().Contain("rid=req-001");
            renderedLog.Should().Contain("pid=platform-a");
            renderedLog.Should().Contain("uflag=u-flag");
            renderedLog.Should().Contain("dur=85ms");
            renderedLog.Should().Contain("msg=HTTP POST /logs/headers -> 202 in 85ms");
        }

        public void Dispose()
        {
            LogManager.Configuration = null;
            LogManager.Shutdown();
        }

        private static MSLoggerMiddleware CreateMiddleware(LoggingTestContext testContext, RequestDelegate next)
        {
            var logger = testContext.Host.Services.GetRequiredService<ILogger<MSLoggerMiddleware>>();
            var timeProvider = testContext.Host.Services.GetRequiredService<TimeProvider>();
            return new MSLoggerMiddleware(next, timeProvider, logger);
        }

        private static LoggingTestContext CreateLoggingContext(string layout)
        {
            var timeProvider = new ControlledTimeProvider(DateTimeOffset.Parse("2026-06-01T00:00:00Z"));

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSingleton<TimeProvider>(timeProvider);
            builder.Services.AddMSLoggerService().WithNLogger(_ => { });
            builder.ConfigurePlatformLogging(configure => configure.NLogConfiguration(global::Microsoft.Extensions.Logging.LogLevel.Information));

            var host = builder.Build();

            var memoryTarget = new MemoryTarget("memory")
            {
                Layout = layout,
            };

            var configuration = new LoggingConfiguration();
            configuration.AddTarget(memoryTarget);
            configuration.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Info, memoryTarget));
            LogManager.Configuration = configuration;
            LogManager.ReconfigExistingLoggers();

            return new LoggingTestContext(host, memoryTarget, timeProvider);
        }

        private sealed class LoggingTestContext : IDisposable
        {
            public LoggingTestContext(IHost host, MemoryTarget memoryTarget, ControlledTimeProvider timeProvider)
            {
                Host = host;
                MemoryTarget = memoryTarget;
                TimeProvider = timeProvider;
            }

            public IHost Host { get; }
            public MemoryTarget MemoryTarget { get; }
            public ControlledTimeProvider TimeProvider { get; }
            public System.Collections.Generic.IList<string> Logs => MemoryTarget.Logs;

            public DefaultHttpContext CreateHttpContext(string method, string path)
            {
                var context = new DefaultHttpContext
                {
                    RequestServices = Host.Services,
                };

                context.Request.Method = method;
                context.Request.Path = path;
                context.Response.Body = new MemoryStream();

                var accessor = Host.Services.GetRequiredService<IHttpContextAccessor>();
                accessor.HttpContext = context;

                return context;
            }

            public void Flush() => LogManager.Flush();

            public void Dispose()
            {
                var accessor = Host.Services.GetRequiredService<IHttpContextAccessor>();
                accessor.HttpContext = null;
                Host.Dispose();
            }
        }

        private sealed class ControlledTimeProvider : TimeProvider
        {
            private DateTimeOffset _utcNow;
            private long _timestamp;

            public ControlledTimeProvider(DateTimeOffset utcNow)
            {
                _utcNow = utcNow;
                _timestamp = 0;
            }

            public override DateTimeOffset GetUtcNow() => _utcNow;

            public override long GetTimestamp() => _timestamp;

            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public void Advance(TimeSpan duration)
            {
                _utcNow = _utcNow.Add(duration);
                _timestamp += duration.Ticks;
            }
        }
    }
}