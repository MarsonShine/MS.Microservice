using FluentValidation;
using MS.Microservice.Core.Reflection;
using MS.Microservice.Domain.Exception;

namespace MS.Microservice.Web.Infrastructure.Mediator.Behaviors
{
    /// <summary>
    /// Validator utility for Wolverine handlers. 
    /// Can be called directly from handlers or used as a middleware pattern.
    /// </summary>
    public class ValidatorMiddleware
    {
        /// <summary>
        /// Validates a message using FluentValidation validators
        /// </summary>
        public static async Task ValidateAsync<T>(
            T message,
            ILogger logger,
            IValidator<T>[]? validators = null)
        {
            if (validators == null || validators.Length == 0)
            {
                return;
            }

            var typeName = TypeHelper.GetGenericTypeName(message!);

            logger.LogInformation("----- Validating command {CommandType}", typeName);

            var failures = validators
                .Select(v => v.Validate(message))
                .SelectMany(result => result.Errors)
                .Where(error => error != null)
                .ToList();

            if (failures.Any())
            {
                logger.LogWarning("Validation errors - {CommandType} - Command: {@Command} - Errors: {@ValidationErrors}", typeName, message, failures);

                throw new DomainException(
                    $"Command Validation Errors for type {typeof(T).Name}", new ValidationException("Validation exception", failures));
            }
        }
    }
}
