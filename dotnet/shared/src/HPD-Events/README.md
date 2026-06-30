# HPD.Events

`HPD.Events` is the shared event foundation for HPD frameworks. It provides semantic class events, fan-out coordination, caller-owned inboxes, inbox-backed async streams, request/response sessions, replay helpers, process-local struct event lanes, local wake signals, and dependency-injection registration.

The package is intentionally infrastructure-only. Domain packages own their event types, authorization, redaction, transport projection, tenancy, and persistence policy.

## Install

```bash
dotnet add package HPD-Events
```

## Event Families

HPD.Events has five related surfaces:

- Semantic class events: `Event`, `IEventCoordinator`, `EventCoordinator`, `IEventBus`
- Async stream contracts: `AsyncStream<TItem>`, `IAsyncStreamSource<TRequest,TItem>`, `IEventStreamSource<TEvent>`
- Replay helpers: `IReplaySource<TEvent>`, `IEventStore<TEvent>`, `ReplayTimeline<TEvent>`
- Struct events: `HPD.Events.Struct` for process-local hot-path samples and frames
- Signals and mailboxes: `HPD.Events.Signals` for local event-loop wakeups

Use the smallest surface that matches the job. Do not use struct events as a faster semantic bus, and do not use signals as a domain event model.

## Semantic Events

Create domain events by inheriting from `Event`.

```csharp
public sealed record NodeCompletedEvent : Event
{
    public required string GraphId { get; init; }
    public required string NodeId { get; init; }
    public required bool Succeeded { get; init; }
    public required TimeSpan Duration { get; init; }
}
```

`Event` includes routing and timing fields:

- `Channel`
- `Kind`
- `Direction`
- `SequenceNumber`
- `EventFlowId`
- `CanInterrupt`
- `Timestamp`
- `ExchangeTimestampNs`

`Event` does not include an arbitrary `Extensions`, `Metadata`, or `Properties` bag. If data matters to the event, put it on the event as a typed property.

## Annotations

Use annotations only for sparse scalar metadata that is not part of the primary event fact, such as diagnostic or projection hints.

```csharp
public sealed record DiagnosticEvent : Event, IAnnotatedEvent
{
    public required string Message { get; init; }

    public IReadOnlyList<EventAnnotation> Annotations { get; init; } =
    [
        new()
        {
            Key = "display",
            Value = EventAnnotationValue.FromBoolean(true),
            Visibility = EventAnnotationVisibility.Public
        }
    ];
}
```

Annotation values are source-generation-friendly scalars: string, integer, number, or boolean. Annotation visibility is only a projection hint, not an authorization policy.

## Coordinator

`EventCoordinator` is the primary semantic event facade. It implements:

- `IEventCoordinator`
- `IEventBus`
- `IEventPublisher`
- `IEventObserverBus`
- `IEventInboxSource`
- `IRequestResponseBus`
- `IHierarchicalEventBus`

```csharp
using HPD.Events.Core;

using var events = new EventCoordinator();

using var subscription = events.Subscribe<NodeCompletedEvent>(evt =>
{
    Console.WriteLine($"{evt.NodeId}: {evt.Succeeded}");
    return ValueTask.CompletedTask;
});

events.Emit(new NodeCompletedEvent
{
    GraphId = "graph-1",
    NodeId = "node-1",
    Succeeded = true,
    Duration = TimeSpan.FromMilliseconds(42)
});
```

Handlers run from subscriber mailboxes. Publishing means the event was accepted into matching mailboxes; it does not mean every handler has finished.

## Inboxes

Use `EventInbox<TEvent>` for low-level local consumption when the caller should own the reader loop and subscription lifetime.

```csharp
await using var inbox = events.CreateInbox<NodeCompletedEvent>();

events.Emit(new NodeCompletedEvent
{
    GraphId = "graph-1",
    NodeId = "node-1",
    Succeeded = true,
    Duration = TimeSpan.Zero
});

await foreach (var evt in inbox.Reader.ReadAllAsync(cancellationToken))
{
    Console.WriteLine(evt.NodeId);
    break;
}
```

Use direct inboxes for private loops, deterministic tests, request-local observation, and code that needs exact subscription ownership.

## Event Streams

Use `IEventStreamSource<TEvent>` or `EventStreamSource<TEvent>` for public, host-facing, transport-facing, UI-facing, connector-facing, and framework-facing live event feeds.

```csharp
var source = new EventStreamSource<NodeCompletedEvent>(events);

var opened = await source.OpenAsync(new EventStreamRequest<NodeCompletedEvent>
{
    StreamId = "graph.nodes",
    Channel = EventChannel.Synchronous,
    Capacity = 1024,
    Backpressure = AsyncStreamBackpressureMode.Wait
}, cancellationToken);

if (!opened.Succeeded || opened.Value is null)
    throw new InvalidOperationException(opened.Error?.Message);

await foreach (var evt in opened.Value.Items.WithCancellation(cancellationToken))
{
    Console.WriteLine(evt.NodeId);
}
```

Event streams are live and inbox-backed. They are not replayable, resumable, durable, authorized, redacted, or transport-specific. Higher-level packages add those policies.

## Dependency Injection

Use `AddHPDEvents()` for host integration.

```csharp
using HPD.Events.DependencyInjection;

services.AddHPDEvents();
```

The default lifetime is singleton. This is the right default for app-level event spines.

Use scoped registration for request-local event buses, such as auth endpoint/audit flows.

```csharp
services.AddHPDEvents(options =>
{
    options.Lifetime = HPDEventsServiceLifetime.Scoped;
});
```

Optional registrations are enabled by default:

```csharp
services.AddHPDEvents(options =>
{
    options.RegisterStructEvents = true;
    options.RegisterEventStreams = true;
});
```

All class-event interfaces resolve through the same `EventCoordinator` instance within the selected lifetime.

## Request/Response

Request/response sessions are events with correlation fields.

```csharp
public sealed record ApprovalRequest : Event, IRequestEvent
{
    public required string RequestId { get; init; }
    public required string SourceName { get; init; }
    public required string Prompt { get; init; }
}

public sealed record ApprovalResponse : Event, IResponseEvent
{
    public required string RequestId { get; init; }
    public required string SourceName { get; init; }
    public required bool Approved { get; init; }
}
```

```csharp
using var responder = events.Subscribe<ApprovalRequest>(request =>
{
    events.Respond(new ApprovalResponse
    {
        RequestId = request.RequestId,
        SourceName = "operator",
        Approved = true
    });

    return ValueTask.CompletedTask;
});

var response = await events.RequestAsync<ApprovalRequest, ApprovalResponse>(
    new ApprovalRequest
    {
        RequestId = Guid.NewGuid().ToString("N"),
        SourceName = "workflow",
        Prompt = "Continue?"
    },
    TimeSpan.FromSeconds(30),
    cancellationToken);
```

## Hierarchy

Coordinators can bubble events to a parent coordinator.

```csharp
using var parent = new EventCoordinator();
using var child = new EventCoordinator();

child.SetParent(parent);
```

Use hierarchy for nested runtimes, agent subflows, graph execution trees, and workflow scopes that need parent-level observation.

## Event Flows

Event flows group interruptible events.

```csharp
using var flow = events.EventFlows.Create();

events.Emit(new NodeCompletedEvent
{
    EventFlowId = flow.EventFlowId,
    GraphId = "graph-1",
    NodeId = "node-1",
    Succeeded = true,
    Duration = TimeSpan.Zero
});

flow.Interrupt();
```

Events with `CanInterrupt = false` still deliver after interruption.

## Replay

Replay is separate from live delivery.

```csharp
var store = new InMemoryEventStore<NodeCompletedEvent>();
await store.AppendAsync(new NodeCompletedEvent
{
    GraphId = "graph-1",
    NodeId = "node-1",
    Succeeded = true,
    Duration = TimeSpan.Zero
});

var timeline = ReplayTimeline<NodeCompletedEvent>
    .Create()
    .AddSource("store", store);

await foreach (var evt in timeline.ReadAsync(ReplayReadOptions.All, cancellationToken))
{
    Console.WriteLine(evt.NodeId);
}
```

A replay source can publish into an event publisher, but the coordinator is not a durable event store.

## Struct Events

Use `HPD.Events.Struct` for process-local hot-path samples and frames.

```csharp
using HPD.Events.Struct;

public readonly record struct QueueDepthSample(int Depth) : IStructEvent
{
    public EventKind Kind => EventKind.Diagnostic;
    public long SequenceNumber => 0;
    public long TimestampNs => 0;
}

using var hub = new StructEventHub();
var route = hub.Route<QueueDepthSample>();
using var inbox = route.CreateInbox();
var emitter = route.CreateEmitter();

emitter.Emit(new QueueDepthSample(12));

if (inbox.TryRead(out var sample))
    Console.WriteLine(sample.Depth);
```

Struct events are not automatically serialized, replayed, transported, or mirrored into semantic class events.

## Signals And Mailboxes

Use signals and mailboxes for local scheduling loops.

```csharp
var signal = new EventSignal();
signal.Signal();
await signal.WaitAsync(cancellationToken);

await using var mailbox = new EventLoopMailbox<string>();
mailbox.TryWrite("work");
await mailbox.WaitToReadAsync(cancellationToken);
```

Signals and mailboxes are coordination primitives, not semantic event feeds.

## Boundaries

HPD.Events does not provide:

- ASP.NET endpoints
- SSE/WebSocket/SignalR/gRPC transports
- authorization
- tenant filtering
- redaction
- durable event sourcing
- domain event types
- automatic class-event/struct-event bridges

Build those in consuming packages where domain policy is known.

## Native AOT

The package is AOT-compatible. The local AOT smoke test exercises class events, inboxes, event streams, request/response, replay, event store, struct events, signals, source-generated JSON, annotations, and DI.
