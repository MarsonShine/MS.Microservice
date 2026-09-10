# Laboratory persistence

Activation identity/log mappings and their existing migration history belong to the laboratory. They are not a dependency of reusable EF Core components or the production reference service. This preserves the old practice database while allowing independent applications to reuse the generic query helpers.

Use the separate reference migrator for production-reference databases. Do not apply reference migrations to an existing Activation practice database.
