# ASP.NET Core host components

`ServiceHost.CreateBuilder(args)` loads deployed settings from the application directory. `AddPlatformHttp` / `UsePlatformHttp` supply trace-correlated ProblemDetails, metadata-only exception logging, explicit proxy trust and an opt-in CORS origin allowlist.

`AddExternalIdentity` validates JWTs through the host's OIDC Authority and Audience. It never issues tokens or maintains passwords. Production metadata requires HTTPS. Configure subject, role and permission claim names under `Authentication`; the reference policies use case-sensitive OAuth scopes `profiles.manage` and `messaging.manage`. A missing or empty subject cannot access protected resources.

Hosts must call authentication and authorization middleware and explicitly allow anonymous access only where intended (for example, liveness). Development may use an HTTP identity provider. TestServer tests use static test signing configuration and do not require external identity services.
