namespace MS.Microservice.Infrastructure.DependencyInjection;

public enum InfrastructureProfile
{
    Production,
    Sample
}

public sealed class InfrastructureModuleOptions
{
    public bool MessagingEnabled { get; private set; }
    public bool EfCorePersistenceEnabled { get; private set; }
    public bool SqlSugarPersistenceEnabled { get; private set; }
    public bool EventSourcingEnabled { get; private set; }
    public bool TelemetryEnabled { get; private set; }

    public InfrastructureModuleOptions UseMessaging()
    {
        MessagingEnabled = true;
        return this;
    }

    public InfrastructureModuleOptions UseEfCorePersistence()
    {
        EfCorePersistenceEnabled = true;
        return this;
    }

    public InfrastructureModuleOptions UseSqlSugarPersistence()
    {
        SqlSugarPersistenceEnabled = true;
        return this;
    }

    public InfrastructureModuleOptions UseEventSourcing()
    {
        EventSourcingEnabled = true;
        return this;
    }

    public InfrastructureModuleOptions UseTelemetry()
    {
        TelemetryEnabled = true;
        return this;
    }

    public InfrastructureModuleOptions UseProductionDefaults()
        => UseMessaging()
            .UseEfCorePersistence()
            .UseTelemetry();

    public InfrastructureModuleOptions UseSampleDefaults()
        => UseMessaging()
            .UseEfCorePersistence()
            .UseSqlSugarPersistence()
            .UseEventSourcing()
            .UseTelemetry();
}
