# Production reference host

This host manages external-identity profiles, audit records, and a small order workflow. It contains no local password login, token issuer, file experiments, AI scenarios, or Lab controllers. All profile administration requires the configured `profiles.manage` scope; order routes require authentication and order reads are limited to the owner; message operations require `messaging.manage`.

Required settings are `ConnectionStrings:ReferenceDatabase`, `Messaging:RabbitMQ:ConnectionString`, `Authentication:Authority`, and `Authentication:Audience`. Supply secrets from the environment. `Messaging:Provider` defaults to `SelfManaged`; `Wolverine` selects the alternative provider. Unknown values fail startup. Apply the selected provider's required business and messaging migrations before starting the service; the host does not create or upgrade its database. In both migration streams, `AddHttpIdempotency` precedes `AddOrders`. A standard full migration creates the idempotency table even when the feature is disabled; only the disabled request path avoids querying it.

`Http:RateLimiting:Enabled` is `false` in the sample configuration. When enabled, each authenticated `(issuer, subject)` gets a per-process fixed-window limit of 120 authorized API requests per 60 seconds with no queue. Anonymous and forbidden requests do not consume a user's quota; health routes do not use it. Set `Enabled` to `true` and choose `PermitLimit` and `WindowSeconds` for the deployment. See [HTTP 入口限流](../../../src/MS.Microservice.AspNetCore/docs/rate-limiting.md) for policy behavior and the multi-instance limit.

`Http:RequestTimeouts:Enabled` is `false` in the sample configuration. When enabled, API requests have a 30-second deadline by default; `Seconds` must be positive. Health routes do not use that deadline. Downstream calls must honor the request cancellation token, and a `504` does not prove a write was rolled back. See [HTTP 请求超时](../../../src/MS.Microservice.AspNetCore/docs/request-timeouts.md).

`Http:Idempotency:Enabled` defaults to `false` and controls registration of the idempotency store and cleanup worker. Each endpoint must opt in: `POST /api/v1/profiles` uses `.RequireHttpIdempotency("profiles.create")` and `POST /api/v1/orders` uses `.RequireHttpIdempotency("orders.create")`. When the global setting is off, both routes ignore `Idempotency-Key`, including malformed or repeated values, and follow their normal create paths without querying the idempotency table. Readiness ignores the two known `AddHttpIdempotency` migrations in this mode, but still requires `AddOrders` and the other migrations for the selected provider. A repeated profile create can return `409` from the business uniqueness rule; a repeated order create can create a second order.

When the global setting is on, only marked routes handle `Idempotency-Key`. The same route wrapper and executor run the normal Profile or Order endpoint inside the idempotency transaction; neither API has its own idempotency Handler. A successful create saves its actual `201`, `Location`, and body. Profile and Order have separate operation names, so the same key can be used independently for each. The fingerprint includes method, path, query, the full content type, and body. JSON object properties are recursively sorted and whitespace outside strings is ignored, while array order remains significant; non-JSON bodies use their original bytes. Reordered JSON properties replay the first response, while changing body values, array order, query, or content type returns `409`. Duplicate JSON property names, including case-only duplicates, return `400` before binding. Unkeyed requests retain their existing behavior; other routes ignore the header. No global idempotency middleware checks every request.

The executor replays `204` with an empty body and no content type, as the MVC test demonstrates. It stops buffering a response above 64 KiB and rolls back the transaction. Apply the selected provider's HTTP idempotency migration before enabling the feature; the host never creates the table automatically. An existing `profiles.create` record written with the former fingerprint conflicts with the new fingerprint for the same identity and key: the request receives `409`, and the endpoint does not run. No legacy lookup or record conversion takes place. See [Profile 与 Order 如何共用 HTTP 幂等](http-idempotency.md) for the shared execution and transaction boundaries, and [Profile 与 Order 的重复 HTTP 请求](idempotent-profile-create.md) for request examples and deployment details.

List endpoints validate pagination query ranges before calling repositories. Their omitted values remain `skip=0`, `take=50` and `limit=100`. Application errors use the shared ProblemDetails mapping, which preserves known public codes and hides unknown 500 details. The [HTTP error and validation note](../../../src/MS.Microservice.AspNetCore/docs/application-errors.md) records the .NET 10 binding behavior behind this choice.

| Route | Purpose |
| --- | --- |
| `/health/live` | Process liveness, anonymous. |
| `/health/ready` | Applied migrations and message storage; broker outage reports degraded while durable writes remain possible. |
| `/api/v1/profiles` | Create/list profiles. |
| `/api/v1/profiles/{id}` | Read/update a profile with `ExpectedVersion`. |
| `/api/v1/orders` | Create an order for the authenticated identity. |
| `/api/v1/orders/{id}` | Read an owned order; another identity receives `404`. |
| `/api/v1/me` | Read the authenticated external identity's local profile. |
| `/api/v1/roles` | Business role catalog. |
| `/api/v1/audit` | Query persisted audit effects. |
| `/api/operations/messages/failures` | List failed-message metadata. |
| `/api/operations/messages/failures/{failureId}/replay` | Submit an authorized replay. |

The test project executes the same HTTP pipeline with SQLite and static test identity metadata. It replaces external background services and broker connections; separate integration tests verify actual PostgreSQL/RabbitMQ behavior.

The health endpoints use shared ASP.NET Core health-check wiring. Liveness never queries external dependencies. Readiness checks database migrations, message storage and Broker with five-second probe deadlines; while HTTP idempotency is off, only the two known idempotency migrations are excluded from the pending-migration check. After shutdown begins, readiness immediately returns `503` with reason `stopping`. The existing `healthy`, `degraded`, `pending_migrations` and `storage_unavailable` responses remain available. See [存活与就绪检查](../../../src/MS.Microservice.AspNetCore/docs/health-checks.md).
