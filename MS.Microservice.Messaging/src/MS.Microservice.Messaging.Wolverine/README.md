# Wolverine reliable messaging adapter

This provider replaces the self-managed Inbox/Outbox as a whole. Business code keeps the same transactional enqueue and handler contracts. Do not register the self-managed worker, receipt middleware, or raw RabbitMQ consumer alongside it.

Each direct application operation uses a fresh native DbContextOutbox over the same business DbContext. Events are captured as serialized snapshots, handed to the native outbox only after the operation succeeds, then saved and flushed through the framework. Native incoming handlers join Wolverine's existing EF transaction; they never create another outbox or commit independently.

The envelope rule maps the stable integration-event Id before persistence and uses registered contract aliases instead of CLR assembly-qualified names. The host must configure durable incoming/outgoing endpoints and IdAndDestination identity for independent subscriptions. Publisher confirmations and persistent storage are mandatory.

Adapter unit tests exercise the common work boundary with SQLite and native interface substitutes. They are not a substitute for the PostgreSQL/RabbitMQ provider contract and crash-recovery tests.

Wolverine 5.31.0's native RabbitMQ sender uses `mandatory=false`. This adapter registers a send-only `ms-rabbitmq` transport through Wolverine's public Endpoint/ISender extension points. It keeps Wolverine's durable sending agents and native envelope mapper, but sends through the shared confirmed RabbitMQ channel with `mandatory=true`. Native RabbitMQ listeners still own incoming durability. This adds no second Inbox/Outbox and does not register the self-managed worker.

Register the provider with `builder.Host.UseWolverineMessaging<TContext>(topology, options)`. It registers one business context and native transaction integration; native storage auto-DDL is disabled. Map native envelopes in the application's provider-specific context, and apply the application's migration plus the native message-store schema through the separate migrator.

Native failure operations replay by logical message Id. If that Id failed at several destinations, native replay can restore all those failed deliveries. Already completed subscriptions remain protected by their native Inbox. The native API does not provide a reliable failure timestamp, so `FailedAtUtc` is null rather than a substituted send time. Outgoing transport failures remain owned by Wolverine's durable sending agents and recovery; native dead-letter operations describe its incoming failure store.

The adapter excludes implementations of IIntegrationEventHandler<T> from Wolverine's conventional
Handler/Consumer discovery. They are invoked only through the registered transactional bridge.
Do not explicitly register those business types as native Wolverine handlers. Ordinary local commands
continue to use Wolverine discovery, including in the Lab host.
