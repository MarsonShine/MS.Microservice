# Reference persistence

`SelfManagedReferenceDbContext` and `WolverineReferenceDbContext` share business mappings but have separate messaging models and migration histories. Both commit business and message changes in the same database. The default database is a new reference database; these migrations do not modify the older Activation teaching database.

The profile identity `(Issuer, Subject)` and optimistic `Version` are enforced by the database. Audit storage has a message key and a unique business-effect key `(ProfileId, ProfileVersion, Consumer)`. Roles use tracked child entities; unchanged role objects are retained during updates.

Business/event occurrence times that must round-trip exactly use UTC ticks. Provider-specific operational timestamps retain their native representation.

Generate migrations through the design-time factories. They build models without connecting to a database. The library emits a runtime configuration so it can serve as the EF CLI startup project:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations script --idempotent --context SelfManagedReferenceDbContext --project samples/Reference/MS.Microservice.Reference.Persistence --startup-project samples/Reference/MS.Microservice.Reference.Persistence --output artifacts/reference-self.sql
```

Use the corresponding context for Wolverine. Its native envelope mappings are excluded from EF migrations: the separate migrator must also export/apply Wolverine's native message-store schema, including node and dead-letter tables. Do not consider the business EF script sufficient for that provider.
