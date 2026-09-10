# Wolverine reliable messaging adapter

This provider replaces the self-managed Inbox/Outbox as a whole. Business code keeps the same transactional enqueue and handler contracts. Do not register the self-managed worker, receipt middleware, or raw RabbitMQ consumer alongside it.

Each direct application operation uses a fresh native DbContextOutbox over the same business DbContext. Events are captured as serialized snapshots, handed to the native outbox only after the operation succeeds, then saved and flushed through the framework. Native incoming handlers join Wolverine's existing EF transaction; they never create another outbox or commit independently.

The envelope rule maps the stable integration-event Id before persistence and uses registered contract aliases instead of CLR assembly-qualified names. The host must configure durable incoming/outgoing endpoints and IdAndDestination identity for independent subscriptions. Publisher confirmations and persistent storage are mandatory.

Adapter unit tests exercise the common work boundary with SQLite and native interface substitutes. They are not a substitute for the PostgreSQL/RabbitMQ provider contract and crash-recovery tests.
