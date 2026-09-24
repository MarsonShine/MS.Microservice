# ASP.NET Core encrypted model binding

This optional component binds versioned RSA-OAEP + AES-GCM request envelopes to explicitly registered MVC models. It depends on Core cryptology; the base AspNetCore host component does not depend on it.

See [加密请求怎样绑定为 MVC 模型](docs/encrypted-model-binding.md) for the problem, wire format, registration, examples and limits.

The binder clears its temporary AES key byte array after use. [托管缓冲区清零](../../docs/ZeroMemory-In-Managed-Services.md) explains what that protects and what remains in memory.
