# HPD-Agent.Client

Invariant .NET 10 runtime used by generated HPD Agent clients.

It provides:

- immutable client options and default headers;
- `HttpClient` ownership and timeout policy;
- bounded retries for replay-safe requests;
- `Retry-After` and `Retry-After-Ms` handling;
- typed connection metadata on API failures;
- raw response access;
- multipart and binary response helpers;
- observable committed-SSE reconnect and cursor resume;
- `IAsyncEnumerable<HpdAgentSseMessage>` with lossless raw data.

Application-specific event/input unions, DTOs, serializer metadata, service
facades, and API snapshots are emitted by `HPD-Agent.ClientGenerator`.
