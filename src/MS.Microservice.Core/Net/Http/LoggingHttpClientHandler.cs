using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Net.Http
{
    /// <summary>
    /// 通过 IHttpClientFactory 注入 LoggingHttpClientHandler
    /// </summary>
    public class LoggingHttpClientHandler : DelegatingHandler
    {
        private readonly ILogger<LoggingHttpClientHandler> _logger;

        public LoggingHttpClientHandler(ILogger<LoggingHttpClientHandler> logger)
        {
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var guid = Guid.NewGuid();
            _logger.LogInformation("【{Guid}】method:【{Method}】log request: 【{request}】", guid, request.Method.Method, request.RequestUri);
            var respnse = await base.SendAsync(request, cancellationToken);
            _logger.LogInformation("【{Guid}】method:【{Method}】log request: 【{response}】", guid, request.Method.Method, respnse.Content.ReadAsStringAsync(cancellationToken));
            return respnse;
        }
    }
}
