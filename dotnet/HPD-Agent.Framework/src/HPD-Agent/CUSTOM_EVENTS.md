# Custom Agent Events

`AgentEvent` is the output-event contract. Applications and capability modules
can define their own concrete events without editing the framework's central
event list.

Use `AgentInputEvent` for inbound work. Do not use `AgentEvent` as a client
request type.

## Define an event

```csharp
using HPD.Agent;
using HPD.Agent.Serialization;
using System.Text.Json.Serialization;

[EventType("ANALYSIS_COMPLETED")]
[DurableEvent]
public sealed record AnalysisCompletedEvent(
    string DatasetId,
    int RowCount) : AgentEvent;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AnalysisCompletedEvent))]
internal partial class AnalysisJsonContext : JsonSerializerContext;
```

The event source generator:

1. discovers every concrete, closed `AgentEvent` record;
2. validates or infers its SCREAMING_SNAKE_CASE discriminator;
3. requires `JsonTypeInfo` from an assembly-local source-generated context;
4. emits a module fragment containing the discriminator, CLR type, JSON
   metadata, durability, module ID, and optional content policy;
5. registers the module and, for applications, emits one complete application
   event composition.

`[EventType]` is optional when the type name produces a suitable discriminator.
Once an event is published, its discriminator is a versioned wire contract.

## Choose durability

| Declaration | Publication | Journal | Replay |
| --- | --- | --- | --- |
| `AgentEvent` only | Live observers receive it | Not admitted | Not replayed |
| `[DurableEvent]` | Live observers receive the committed value | Admitted with an event ID and thread sequence | Replayed from the thread journal |

Live-only is the default because most progress and diagnostic events should not
become permanent transcript history. Mark an event durable only when it is part
of the thread's stable, replayable meaning.

Removing a durable event from the deployed composition can make existing thread
journals unreadable. Use a migration or retain a compatible decoder instead of
silently deleting durable contracts.

## Emit an event

Middleware and function code can publish through their execution context:

```csharp
await context.PublishAsync(
    new AnalysisCompletedEvent(datasetId, rowCount),
    cancellationToken);
```

With thread scope, `PublishAsync` routes according to the event descriptor:

- live-only goes directly to observers with thread sequence zero;
- durable is committed to the thread journal first, then published with its
  authoritative sequence number.

Use `PublishLiveAsync` to force observation without journal mutation. It still
requires the event to be present in the active event composition and therefore
cannot bypass schema registration.

Runtime infrastructure can use `IAgentEventPublisher` directly when it already
owns an explicit thread key. The store's event codec remains the sole
serialization authority.

## Delivery and replay

The HTTP SSE endpoint emits:

```json
{
  "event": {
    "version": "1.0",
    "type": "ANALYSIS_COMPLETED",
    "datasetId": "d-123",
    "rowCount": 42
  },
  "route": {
    "origin": {
      "sessionId": "session-1",
      "threadId": "main"
    },
    "path": [
      {
        "sessionId": "session-1",
        "threadId": "main"
      }
    ]
  }
}
```

Committed events replay first, followed by live delivery. Duplicate live copies
of already replayed committed events are suppressed by journal sequence.
Thread hierarchy is retained in the route, while each thread keeps its own
journal cursor.

The event catalog exposes the discriminator, contract name, durability, module,
and composition digest:

```text
GET /api/hpd-agent/event-catalog
```

The OpenAPI SSE payload is generated from that same event composition. Its
event union includes custom concrete events, and each schema carries
`x-hpd-event-discriminator`, `x-hpd-event-durability`, and
`x-hpd-event-module` metadata. `x-hpd-event-digest` on the SSE operation matches
the catalog digest.

## Missing, old, and unknown events

- Missing source-generated JSON metadata fails compilation with `HPDAEVT002`.
- An invalid or duplicate discriminator fails with `HPDAEVT005`.
- An event absent from the active codec is rejected before publication.
- A live event with an unknown wire discriminator throws
  `UnknownAgentEventDiscriminatorException`.
- An unknown persisted durable event throws
  `UnknownDurableAgentEventException` with session, thread, generation, sequence,
  discriminator, and codec digest. The exception intentionally omits the raw
  payload.

Fail-closed replay prevents an application from silently rewriting history.
Generate a new deployment with the old event module, or perform an explicit
migration, before reading that journal.

Clients should still preserve unknown output events as raw JSON. A newer server
may add an optional or application-specific event after a client SDK was
generated. Unknown-event preservation provides forward compatibility; it does
not grant authority to reinterpret the event.

## Content retention

`[PersistEventContent]` can archive a canonical serialized event in the content
store after successful live or durable publication. This is useful for large
payloads that should not sit inline in every journal frame. It is independent of
journal durability.

## Compatibility rules

- Treat the discriminator and CLR contract identity as stable.
- Adding an optional property is backward compatible.
- Renaming or removing a property, changing its meaning, or changing its type is
  a contract change.
- Never reuse a retired discriminator for another CLR type.
- Keep the event catalog digest in generated client metadata.
- Keep the full generated event composition aligned between runtime, store,
  replay, and SDK generation.

