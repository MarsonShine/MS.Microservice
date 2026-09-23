# Production reference host

This host manages external-identity profiles and audit records. It contains no local password login, token issuer, file experiments, AI scenarios, or Lab controllers. All profile administration requires the configured `profiles.manage` scope; message operations require `messaging.manage`.

Required settings are `ConnectionStrings:ReferenceDatabase`, `Messaging:RabbitMQ:ConnectionString`, `Authentication:Authority`, and `Authentication:Audience`. Supply secrets from the environment. `Messaging:Provider` defaults to `SelfManaged`; `Wolverine` selects the alternative provider. Unknown values fail startup. Apply the selected provider's migrations before starting the service; the host does not create or upgrade its database.

`Http:RateLimiting:Enabled` is `false` in the sample configuration. When enabled, the API routes share a per-process fixed-window limit of 120 requests per 60 seconds with no queue; health routes do not consume this quota. Set `Enabled` to `true` and choose `PermitLimit` and `WindowSeconds` for the deployment. See [HTTP 入口限流](../../../src/MS.Microservice.AspNetCore/docs/rate-limiting.md) for policy behavior and the multi-instance limit.

| Route | Purpose |
| --- | --- |
| `/health/live` | Process liveness, anonymous. |
| `/health/ready` | Applied migrations and message storage; broker outage reports degraded while durable writes remain possible. |
| `/api/v1/profiles` | Create/list profiles. |
| `/api/v1/profiles/{id}` | Read/update a profile with `ExpectedVersion`. |
| `/api/v1/me` | Read the authenticated external identity's local profile. |
| `/api/v1/roles` | Business role catalog. |
| `/api/v1/audit` | Query persisted audit effects. |
| `/api/operations/messages/failures` | List failed-message metadata. |
| `/api/operations/messages/failures/{failureId}/replay` | Submit an authorized replay. |

The test project executes the same HTTP pipeline with SQLite and static test identity metadata. It replaces external background services and broker connections; separate integration tests verify actual PostgreSQL/RabbitMQ behavior.
