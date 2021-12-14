using MS.Microservice.Core.Reflection;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Web.Infrastructure.Mediator.Behaviors
{
    public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
        public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = logger;

        public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<TResponse> next)
        {
            _logger.LogInformation("----- Handling command {CommandName} ({@Command})", TypeHelper.GetGenericTypeName(request), request);
            var response = await next();
            _logger.LogInformation("----- Command {CommandName} handled - response: {@Response}", TypeHelper.GetGenericTypeName(request), response);

            return response;
        }
    }
}
