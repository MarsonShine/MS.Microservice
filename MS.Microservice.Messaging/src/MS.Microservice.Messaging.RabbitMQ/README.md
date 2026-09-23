# RabbitMQ transport

This optional transport serves the self-managed reliable messaging provider. It does not own an Inbox or Outbox. Publishing uses persistent messages, a stable AMQP `MessageId`, publisher confirmations with tracking, and `mandatory=true`. A return is a permanent routing failure; a nack or disconnected confirmation remains a failed/unknown publication, never success.

The JSON body is the event payload. `ms-contract-name`, `ms-contract-version`, and `ms-occurred-at` identify its registered contract and exact UTC occurrence time. W3C tracing headers and correlation identifiers are preserved. Payload and AMQP short-string limits are measured in UTF-8 bytes; Inbox/Outbox metadata column limits are measured in .NET string length. See [message metadata limits](../../docs/message-metadata-limits.md).

The host supplies `RabbitMqOptions.ConnectionString`; the library contains no broker credentials. Declare the expected durable topology using the explicit provisioning entry point before starting an application. Publishing checks that the exchange exists and requires a route, but mandatory publication alone cannot prove every intended subscription binding exists.

Source copies require this project, Messaging.Abstractions, RabbitMQ.Client, and the host's Microsoft.Extensions dependencies. Unit tests use interface substitutes; actual broker recovery belongs to the separate integration test suite.
