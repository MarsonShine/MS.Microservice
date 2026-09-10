namespace MS.Microservice.Messaging.IntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class MessagingIntegrationTheoryAttribute : TheoryAttribute
{
    public MessagingIntegrationTheoryAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_MESSAGING_INTEGRATION_TESTS"),
                "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Set RUN_MESSAGING_INTEGRATION_TESTS=true for mandatory PostgreSQL/RabbitMQ integration checks.";
        // When explicitly enabled, missing Docker or dependencies must fail fixture startup.
    }
}
