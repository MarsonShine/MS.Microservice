# Production reference host

This host manages external-identity profiles and audit records. It contains no local password login, token issuer, file experiments, AI scenarios, or Lab controllers. All profile administration requires the configured `profiles.manage` scope; message operations require `messaging.manage`.

Required settings are `ConnectionStrings:ReferenceDatabase`, `Messaging:RabbitMQ:ConnectionString`, `Authentication:Authority`, and `Authentication:Audience`. Supply secrets from the environment. `Messaging:Provider` defaults to `SelfManaged`; `Wolverine` selects the alternative provider. Unknown values fail startup. Apply the selected provider's migrations before starting the service; the host does not create or upgrade its database.

`Http:RateLimiting:Enabled` is `false` in the sample configuration. When enabled, the API routes share a per-process fixed-window limit of 120 requests per 60 seconds with no queue; health routes do not consume this quota. Set `Enabled` to `true` and choose `PermitLimit` and `WindowSeconds` for the deployment. See [HTTP 入口限流](../../../src/MS.Microservice.AspNetCore/docs/rate-limiting.md) for policy behavior and the multi-instance limit.

`Http:RequestTimeouts:Enabled` is `false` in the sample configuration. When enabled, API requests have a 30-second deadline by default; `Seconds` must be positive. Health routes do not use that deadline. Downstream calls must honor the request cancellation token, and a `504` does not prove a write was rolled back. See [HTTP 请求超时](../../../src/MS.Microservice.AspNetCore/docs/request-timeouts.md).

`POST /api/v1/profiles` accepts an optional `Idempotency-Key`. Successful keyed requests save the `201` response with the profile and Outbox write, so a retry can replay its `Location` and body. A different request using the same key returns `409`. Invalid or conflicting business requests do not reserve the key. Unkeyed requests keep the existing behavior. The selected provider's database migration must be applied before using the header; the host never creates the table automatically. See [创建档案时如何处理重复 HTTP 请求](idempotent-profile-create.md).

List endpoints validate pagination query ranges before calling repositories. Their omitted values remain `skip=0`, `take=50` and `limit=100`. Application errors use the shared ProblemDetails mapping, which preserves known public codes and hides unknown 500 details. The [HTTP error and validation note](../../../src/MS.Microservice.AspNetCore/docs/application-errors.md) records the .NET 10 binding behavior behind this choice.

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

The health endpoints use shared ASP.NET Core health-check wiring. Liveness never queries external dependencies. Readiness checks database migrations, message storage and Broker with five-second probe deadlines; after shutdown begins, it immediately returns `503` with reason `stopping`. The existing `healthy`, `degraded`, `pending_migrations` and `storage_unavailable` responses remain available. See [存活与就绪检查](../../../src/MS.Microservice.AspNetCore/docs/health-checks.md).
