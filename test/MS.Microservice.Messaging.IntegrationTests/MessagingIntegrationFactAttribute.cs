namespace MS.Microservice.Messaging.IntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class MessagingIntegrationFactAttribute : FactAttribute
{
    public MessagingIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_MESSAGING_INTEGRATION_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set RUN_MESSAGING_INTEGRATION_TESTS=true to run PostgreSQL and RabbitMQ Testcontainers.";
        }
    }
}
