using MS.Microservice.Core.Reflection;

namespace MS.Microservice.Web.Infrastructure.Mediator.Behaviors
{
    /// <summary>
    /// Wolverine middleware for logging
    /// </summary>
    public class LoggingMiddleware
    {
        public static void Before<T>(T message, ILogger<LoggingMiddleware> logger)
        {
            logger.LogInformation("----- Handling command {CommandName} ({@Command})", TypeHelper.GetGenericTypeName(message!), message);
        }

        public static void After<T, TResponse>(T message, TResponse response, ILogger<LoggingMiddleware> logger)
        {
            logger.LogInformation("----- Command {CommandName} handled - response: {@Response}", TypeHelper.GetGenericTypeName(message!), response);
        }
    }
}
