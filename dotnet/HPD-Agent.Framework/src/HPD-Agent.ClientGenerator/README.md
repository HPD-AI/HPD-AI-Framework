# HPD-Agent.ClientGenerator

Deterministically generates a standalone C# client from an HPD Agent OpenAPI
3.1 contract.

```bash
dotnet run --project src/HPD-Agent.ClientGenerator -- \
  --input hpd-agent-v1.openapi.json \
  --runtime-catalog runtime-extension-catalog.json \
  --output generated/HPD.Agent.Client \
  --namespace HPD.Agent.Client
```

The generator:

- validates the OpenAPI version, operation IDs, component references, public
  input union, and event union;
- verifies the declared event count and digest;
- promotes inline OpenAPI objects into reusable immutable request/response
  models instead of collapsing compound shapes to `JsonElement`;
- emits discriminated `HpdAgentInputEvent`, `HpdAgentResponseInput`, and
  `HpdAgentEvent` unions;
- emits one service facade per OpenAPI tag;
- emits `client.Live` with typed `On<TEvent>`/`OnAny`/`OnError` subscriptions,
  cursor validation, duplicate suppression, rebase errors, and ordered dispatch;
- emits an AOT-safe client-tool registry with automatic invoke/outcome handling;
- recognizes `101 Switching Protocols` operations and emits typed provider
  connection factories for the protocol-v2 client-tool WebSocket;
- emits `client.Chat` when the standard session, input, runtime-state, and
  execution operations are present;
- emits a public API snapshot and fails regeneration when it changes without an
  explicit `--allow-public-api-change`;
- emits authenticated-header, timeout, raw-response, multipart, binary, and SSE
  building blocks;
- emits extensible string enum wrappers that preserve unknown server values;
- optionally emits strongly typed runtime payload models from the runtime
  extension catalog, plus encode/decode helpers for live, durable, and input
  envelopes;
- emits generated code against a separate invariant runtime project carrying
  retry, error, raw-response, and SSE behavior;
- emits a source-generated `JsonSerializerContext`;
- builds the generated project with warnings as errors and AOT analyzers before
  replacing the destination;
- writes `hpd-client.generated.json` with the generator, OpenAPI, and event
  digests.

The raw SSE surface remains `IAsyncEnumerable<HpdAgentSseMessage>`. A reconnect
retains the last event ID, supplies it as both `after` and `Last-Event-ID`, and
reports every reconnect decision through an observable callback. `client.Live`
adds typed subscriptions and validates each committed `generation:sequence`
cursor before dispatch. Duplicate replay is suppressed and a journal-generation
change raises `HpdAgentJournalRebasedException`.

Retries apply to GET, HEAD, OPTIONS, PUT, and DELETE. POST is never retried
unless the operation contract declares `x-hpd-replay-safe-post: true`. Retry
delays honor `Retry-After-Ms` and `Retry-After`.

## Current boundary

This generator supports object models, arrays, maps, primitives, named schema
references, discriminated HPD event/input/response unions, extensible inline
string enums, and promoted inline object schemas. Schemas that remain true
JSON-Schema unions use `JsonElement` so generated code does not pretend that an
arbitrary union has a closed CLR type. Extension-data maps remain lossless JSON.

The generated project targets .NET 10. Output is split into `runtime/` and
`generated/`: the runtime is invariant across contracts, while generated code
contains only application-specific models, unions, serializer metadata, service
facades, and the public API snapshot.

