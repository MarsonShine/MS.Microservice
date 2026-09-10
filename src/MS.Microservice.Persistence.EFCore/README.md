# EF Core query components

This package contains reusable specification/include evaluation and soft-delete query helpers. It depends on Core and EF Core relational APIs, not on sample domain models, a host, a broker, or a specific database context.

Applications own their DbContext, model mappings and migrations. The old Activation identity/log database is maintained under `samples/Lab/MS.Microservice.Lab.Persistence`; the production reference has its own provider-specific contexts under `samples/Reference`.

Copy this project with Core and Domain.Primitives, or consume their packages together. Choose and configure the database provider in the application.
