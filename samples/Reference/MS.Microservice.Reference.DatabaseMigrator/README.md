# Reference database migrator

The default command exports SQL and SHA-256 manifests only. It does not start the reference service, connect to RabbitMQ, or apply database changes.

```powershell
dotnet run --project samples/Reference/MS.Microservice.Reference.DatabaseMigrator -- --provider SelfManaged --output artifacts/migrations/reference-self
dotnet run --project samples/Reference/MS.Microservice.Reference.DatabaseMigrator -- --provider Wolverine --output artifacts/migrations/reference-wolverine
```

SelfManaged exports the complete idempotent EF script through the current migration. Wolverine also exports its native creation schema through the framework's own schema writer, including incoming, outgoing, dead-letter and node tables. Native creation SQL is for a fresh schema; it does not reconcile an existing installation. For an existing Wolverine deployment, set `ConnectionStrings__ReferenceDatabase` and add `--diff` to export a read-only schema comparison for review.

`--apply` explicitly applies the selected provider's native schema (when applicable) and EF migrations. It requires `ConnectionStrings__ReferenceDatabase`; use a short-lived DDL identity in local development or a separately approved deployment job. `--provision-broker` explicitly creates/binds durable queues and requires `Messaging__RabbitMQ__ConnectionString`. Neither flag is implied by export. Runtime hosts use DML credentials and do not perform these operations.

Review SQL, archive the manifest, apply native Wolverine schema before its business EF script, and verify readiness before enabling traffic. Keep the old Activation teaching database separate. Switching providers does not move in-flight messages; stop writes and drain the old provider before changing configuration.
