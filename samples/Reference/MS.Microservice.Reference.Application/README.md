# Reference application

Profile use cases depend only on the domain, common result types, repository ports and messaging contracts. HTTP and broker-specific types do not appear here. The outer unit of work commits changes; domain events are explicitly mapped to `reference.profile.changed.v1` and cleared after success.

`ProfileAuditHandler` tracks an audit effect without saving its own transaction. Both reliable messaging providers invoke this handler. The persistence adapter must enforce the permanent `(profile, version, consumer)` uniqueness rule in addition to the handler's fast duplicate check.
