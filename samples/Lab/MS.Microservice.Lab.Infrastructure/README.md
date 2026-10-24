# Laboratory infrastructure

This assembly composes laboratory adapters and experiments. Reusable components (observability, audio, Excel, message providers and database query helpers) remain independent projects; the production reference host selects those components explicitly and does not reference this laboratory assembly.

## Event projection

`PostgresEventStore` implements `IEventStore` and the optional `IEventBatchReader`; both DI interfaces resolve to the same scoped adapter. `ReadAllAsync` still returns all matching events. `ReadBatchAsync` applies its 1–1000 event limit in the database query and reads without tracking event records.

`OrderReadModelProjector` requires the supplied store to implement `IEventBatchReader`; it does not fall back to a full-history read. It processes pages of 100 events, loads each page's distinct orders in one query, and reuses one model per order within that page. Read-model changes and the checkpoint commit in the same transaction with the registered checkpoint store, which uses the same `EventStoreDbContext`.

Projection requires a context without pending changes or an existing transaction, including an ambient transaction. Each batch releases only the projection models and checkpoint newly tracked by that batch. Originally tracked unchanged entities retain their references; if a batch rolls back, their captured values and state are restored so the same context can retry without applying deltas twice. Each new batch reads persisted model values before applying events, rather than treating restored memory values as database facts after an uncertain commit. Other tracked event or snapshot entities are not cleared.

Earlier committed batches remain committed when a later batch fails. The loop stops on an empty or short page, so continuing writes can extend catch-up until cancellation. Callers must serialize runs of this projection; concurrent projector instances are outside this contract. This is not a fixed snapshot or distributed projection protocol. PostgreSQL sequence allocation does not guarantee concurrent transaction commit order; SQLite tests do not establish correctness for that separate concurrency case. Migrations and the database schema are unchanged.
