# Laboratory host

This host contains local-identity, file, functional and event-sourcing examples. It does not run inside the production reference host. The default launch profile uses the Lab environment and port 5210.

Supply `LabTokenIssuer__SigningKey` with an externally generated key of at least 32 ASCII characters. `LabTokenIssuer:Issuer`, `Audience` and `LifetimeSeconds` define the active local issuer; validation automatically trusts that active tuple. Optional `IdentityOptions:JwtBearerOption` arrays add trusted issuers/audiences/rotation keys rather than choosing the signing key by array position.

The local account/password login is for this laboratory. Password request Base64 is an encoding, not encryption; company services use the separate reference host's external JWT/OIDC integration. Existing Activation/EventStore practice databases remain separate from reference databases.
