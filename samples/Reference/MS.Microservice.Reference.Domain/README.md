# Reference domain

This business model represents local profiles linked to external `(issuer, subject)` identities. It does not store passwords, sign tokens, or depend on a web framework, ORM, or message broker. Business roles are profile data; they do not grant the OAuth scopes that protect administrative APIs.

A real profile change increments its optimistic version and records an in-process domain event. A no-op update does neither. The application maps these events explicitly into versioned integration events and clears them only after a successful transaction.

Auditing uses both message identity and the permanent business key `(profile, version, consumer)` so a logically repeated change cannot create another audit effect after transport receipts expire.
