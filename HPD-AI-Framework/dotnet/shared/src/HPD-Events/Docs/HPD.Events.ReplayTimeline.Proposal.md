# Engineering Proposal: HPD Events Replay Timeline

**Document:** `HPD.Events.ReplayTimeline.Proposal.md`  
**Version:** 1.0.0  
**Date:** May 2026  
**Status:** Proposal  
**Target:** `HPD.Events`  
**Constraint:** Domain-agnostic, zero external package dependencies, AOT/trim compatible

---

## Executive Summary

HPD Events already provides the live event delivery spine:

1. event publishing through `IEventPublisher`
2. observer subscriptions through callback pumps
3. deterministic caller-owned inboxes through `IEventInboxSource`
4. coordinator-assigned `SequenceNumber`
5. external high-resolution `ExchangeTimestampNs`
6. hierarchical event bubbling
7. interruptible event-flow tracking

What it does not yet provide is a first-class replay model.

Replay is a cross-cutting HPD capability. Finance needs it for market-data backtests. Workflow systems need it for execution reconstruction. Agents need it for trace playback. Graph runtimes need it for deterministic reruns. Telemetry and audit systems need it for reproducing historical event sessions.

This proposal adds a domain-agnostic replay layer to `HPD.Events`:

```text
one or more replay sources
    -> ReplayTimeline<TEvent>
    -> deterministic ordered event stream
    -> optional publish into HPD EventBus
```

The design intentionally keeps replay separate from `IEventCoordinator`. The coordinator owns event delivery. Replay owns event input, ordering, reading, and publishing into a delivery surface.

---

## 1. Design Principles

### 1.1 Replay Is Not Finance-Specific

HPD Events must not learn about:

- quotes
- trades
- bars
- order books
- instruments
- venues
- fills
- strategies

Those belong in Rhodium or other domain packages.

HPD Events should only know:

- events have timestamps
- events can come from multiple sources
- event sources need deterministic merge ordering
- ordered replay can be read directly or published into an event bus

### 1.2 Replay Is Not Event Delivery

`IEventCoordinator` currently owns live event delivery:

```csharp
publisher.Emit(evt);
await using var inbox = events.CreateInbox<Event>();
```

Replay should not be added to that surface. Replay is a source/timeline concern. Delivery remains owned by `IEventPublisher`, `IEventInboxSource`, and the existing event coordinator.

### 1.3 Replay Sources Are Broader Than Event Stores

An event store is useful, but many replay inputs are not stores:

- in-memory test events
- CSV readers
- JSONL files
- parquet or Arrow files
- external API exports
- generated synthetic event streams
- historical domain feeds

Therefore `IReplaySource<TEvent>` is the central abstraction. `IEventStore<TEvent>` is a durable append/read specialization.

### 1.4 Avoid Retrofit Shape

Because HPD is still shaping the core event model, the replay layer should be clean and opinionated:

- explicit read options
- a single obvious `ReplayTimeline<TEvent>` public model
- no nullable options in new APIs
- no finance-specific helper paths
- no manual merger assembly required by common users
- no compatibility shims for old terminology

Breaking changes are acceptable for this architecture. Prefer a coherent model over preserving older names that now carry the wrong meaning.

---

## 2. Current HPD Events Fit

The existing project is a good fit for this addition:

- `HPD-Events.csproj` is packable and has no external package dependencies.
- `Event` already has `Timestamp`, `ExchangeTimestampNs`, and `SequenceNumber`.
- `IEventPublisher` is a narrow emit-only surface.
- `IEventInboxSource` is already the deterministic consumption surface.
- `EventInboxOptions.Deterministic()` already names the caller-owned deterministic event lane.

Replay should extend this model by providing deterministic historical input to those existing surfaces.

---

## 3. Public API

### 3.1 IReplaySource

`IReplaySource<TEvent>` is the universal replay input contract.

```csharp
namespace HPD.Events;

public interface IReplaySource<TEvent>
    where TEvent : Event
{
    IAsyncEnumerable<TEvent> ReadAsync(
        ReplayReadOptions options,
        CancellationToken ct = default);
}
```

Rules:

- Implementations should return events in their natural source order.
- Implementations may apply `ReplayReadOptions` directly when efficient.
- Implementations must respect cancellation.
- Implementations must not publish into an event bus themselves.

### 3.2 IEventStore

`IEventStore<TEvent>` is a replay source that also supports append.

```csharp
namespace HPD.Events;

public interface IEventStore<TEvent> : IReplaySource<TEvent>
    where TEvent : Event
{
    ValueTask AppendAsync(TEvent evt, CancellationToken ct = default);
}
```

This is intentionally small. It is not a full event-sourcing framework. It is the minimal durable event abstraction needed by HPD replay consumers.

### 3.3 IReplayOrderingPolicy

`IReplayOrderingPolicy<TEvent>` creates deterministic ordering keys.

```csharp
namespace HPD.Events;

public interface IReplayOrderingPolicy<TEvent>
    where TEvent : Event
{
    ReplayKey GetKey(
        TEvent evt,
        ReplaySourceInfo source,
        long sourceSequence);
}
```

The policy receives:

- the event
- the source metadata
- the event's ordinal sequence inside that source

Domain packages can provide their own policies. HPD Events ships a generic default policy.

### 3.4 ReplayTimeline

`ReplayTimeline<TEvent>` is the main user-facing replay object.

```csharp
namespace HPD.Events.Core;

public sealed class ReplayTimeline<TEvent>
    where TEvent : Event
{
    public static ReplayTimeline<TEvent> Create();

    public ReplayTimeline<TEvent> AddSource(
        string sourceId,
        IReplaySource<TEvent> source,
        int priority = 0);

    public ReplayTimeline<TEvent> WithOrdering(
        IReplayOrderingPolicy<TEvent> ordering);

    public IAsyncEnumerable<TEvent> ReadAsync(
        ReplayReadOptions options,
        CancellationToken ct = default);

    public Task PublishAsync(
        IEventPublisher publisher,
        ReplayReadOptions options,
        CancellationToken ct = default);
}
```

`ReplayTimeline<TEvent>` is intentionally a fluent builder and replay reader in one object. This makes the common path obvious:

```csharp
var replay = ReplayTimeline<MyEvent>.Create()
    .AddSource("primary", sourceA)
    .AddSource("secondary", sourceB, priority: 10);

await foreach (var evt in replay.ReadAsync(ReplayReadOptions.All, ct))
{
    Process(evt);
}
```

Or:

```csharp
await replay.PublishAsync(events, ReplayReadOptions.All, ct);
```

---

## 4. Public Data Types

### 4.1 ReplayKey

```csharp
namespace HPD.Events;

public readonly record struct ReplayKey(
    long TimestampNs,
    int SourcePriority,
    int EventPriority,
    int SourceOrdinal,
    long SourceSequence,
    long EventSequenceNumber);
```

Ordering is lexicographic:

1. `TimestampNs`
2. `SourcePriority`
3. `EventPriority`
4. `SourceOrdinal`
5. `SourceSequence`
6. `EventSequenceNumber`

Lower values sort first.

`SourceOrdinal` is the zero-based order in which the source was added to the timeline. It is included in the key, rather than hidden in the merge implementation, so same-time events remain deterministic even when multiple sources have the same priority and local source sequence.

### 4.2 ReplaySourceInfo

```csharp
namespace HPD.Events;

public sealed record ReplaySourceInfo(
    string SourceId,
    int Priority,
    int SourceOrdinal);
```

`SourceId` is a stable logical source name. It may be a feed name, file name, stream name, exchange name, subsystem name, or test fixture name.

`Priority` is used for deterministic same-time tie breaking across sources.

`SourceOrdinal` is assigned by `ReplayTimeline<TEvent>` when a source is added.

### 4.3 ReplayReadOptions

```csharp
namespace HPD.Events;

public sealed record ReplayReadOptions(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? EventFlowId,
    int? Limit)
{
    public static ReplayReadOptions All { get; } =
        new(null, null, null, null);
}
```

Rules:

- `From` is inclusive.
- `To` is exclusive.
- `EventFlowId` filters events with a matching `Event.EventFlowId`.
- `Limit` caps the total number of events returned from the read.
- `From` and `To` compare against the same effective timestamp used for replay ordering: `ExchangeTimestampNs` when nonzero, otherwise `Timestamp`.

The initial implementation should filter after source reads for simplicity. Stores and advanced sources may push these filters down.

---

## 5. Built-In Implementations

### 5.1 EnumerableReplaySource

```csharp
public sealed class EnumerableReplaySource<TEvent> : IReplaySource<TEvent>
    where TEvent : Event
{
    public EnumerableReplaySource(IEnumerable<TEvent> events);
}
```

Use cases:

- tests
- fixtures
- small in-memory replay inputs
- adapters around already materialized event lists

### 5.2 AsyncEnumerableReplaySource

```csharp
public sealed class AsyncEnumerableReplaySource<TEvent> : IReplaySource<TEvent>
    where TEvent : Event
{
    public AsyncEnumerableReplaySource(IAsyncEnumerable<TEvent> events);
}
```

Use cases:

- existing async loaders
- generated streams
- file readers
- external API adapters

### 5.3 InMemoryEventStore

```csharp
public sealed class InMemoryEventStore<TEvent> : IEventStore<TEvent>
    where TEvent : Event
{
    public ValueTask AppendAsync(TEvent evt, CancellationToken ct = default);

    public IAsyncEnumerable<TEvent> ReadAsync(
        ReplayReadOptions options,
        CancellationToken ct = default);
}
```

Use cases:

- unit tests
- local deterministic replay
- event-store contract tests
- small process-local event sessions

This implementation should preserve append order and expose it as source order.

### 5.4 DefaultReplayOrderingPolicy

```csharp
public sealed class DefaultReplayOrderingPolicy<TEvent>
    : IReplayOrderingPolicy<TEvent>
    where TEvent : Event
{
    public static DefaultReplayOrderingPolicy<TEvent> Instance { get; }
}
```

Default ordering:

```text
timestamp = evt.ExchangeTimestampNs if nonzero
         else evt.Timestamp converted to Unix nanoseconds

ReplayKey(
    TimestampNs: timestamp,
    SourcePriority: source.Priority,
    EventPriority: 0,
    SourceOrdinal: source.SourceOrdinal,
    SourceSequence: sourceSequence,
    EventSequenceNumber: evt.SequenceNumber)
```

### 5.5 ReplayTimeline

`ReplayTimeline<TEvent>` performs the deterministic merge.

Implementation approach:

- each source is read through an async enumerator
- each enumerator contributes at most one current event to a priority queue
- each queued item carries `ReplayKey`, source metadata, source sequence, and event
- the lowest key is emitted
- the winning source advances one event
- the process repeats until all sources are exhausted or the read limit is reached

The merge must be stable and deterministic across runs.

### 5.6 SystemClock and ManualClock

`IClock` exists today, but HPD Events should ship concrete implementations.

```csharp
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow { get; }
    public long UnixNanos { get; }

    public ITimerHandle SetAlert(
        string name,
        DateTimeOffset alertTime,
        Action<TimeEvent> callback);

    public ITimerHandle SetAlert(
        string name,
        TimeSpan delay,
        Action<TimeEvent> callback);

    public ITimerHandle SetTimer(
        string name,
        TimeSpan interval,
        Action<TimeEvent> callback,
        DateTimeOffset? startTime = null,
        DateTimeOffset? stopTime = null);

    public void CancelTimer(string name);
    public void CancelAllTimers();
    public IEnumerable<string> TimerNames { get; }
}
```

```csharp
public sealed class ManualClock : IClock
{
    public DateTimeOffset UtcNow { get; }
    public long UnixNanos { get; }

    public void Advance(TimeSpan delta);
    public void Set(DateTimeOffset utcNow);

    public ITimerHandle SetAlert(
        string name,
        DateTimeOffset alertTime,
        Action<TimeEvent> callback);

    public ITimerHandle SetAlert(
        string name,
        TimeSpan delay,
        Action<TimeEvent> callback);

    public ITimerHandle SetTimer(
        string name,
        TimeSpan interval,
        Action<TimeEvent> callback,
        DateTimeOffset? startTime = null,
        DateTimeOffset? stopTime = null);

    public void CancelTimer(string name);
    public void CancelAllTimers();
    public IEnumerable<string> TimerNames { get; }
}
```

Replay tests and deterministic hosts should use `ManualClock`. Live code should use `SystemClock`.

These are full `IClock` implementations, not timestamp-only helpers. `ManualClock.Advance` is responsible for firing due one-shot alerts and recurring timers deterministically before returning.

---

## 6. Ordering Semantics

### 6.1 Generic HPD Ordering

The built-in default policy should be domain-agnostic:

```text
Event time
Source priority
Generic event priority
Source ordinal
Source sequence
Coordinator sequence number
```

This means HPD Events can correctly merge traces, graph events, agent events, telemetry, and finance events without knowing domain-specific event meaning.

### 6.2 Domain Ordering

Domain packages may override `EventPriority`.

For example, Rhodium can define:

```text
same timestamp:
    lifecycle/status events
    book updates
    quotes
    trades
    bars
    generated execution events
```

That policy lives in Rhodium, not HPD Events.

### 6.3 Source Ordering Assumption

The first implementation may assume each source is already internally ordered by its own source semantics.

If a source is not ordered, the source adapter is responsible for ordering or materializing its own input. A later `SortingReplaySource<TEvent>` can be added if needed, but it should not be required for the core timeline.

### 6.4 Same-Time Determinism

Same-time events must never depend on dictionary enumeration, task completion races, channel scheduling, or thread timing.

Every tie must resolve through explicit data in `ReplayKey`.

---

## 7. Relationship To Existing Stream Registry

HPD Events currently has `IStreamRegistry` and `IStreamHandle`. These are not replay streams. They are interruptible event-flow tracking primitives.

Replay introduces a second meaning of "stream" unless the terminology is clarified.

Required cleanup:

```text
IStreamRegistry -> IEventFlowRegistry
IStreamHandle   -> IEventFlowHandle
StreamRegistry  -> EventFlowRegistry
StreamHandle    -> EventFlowHandle
```

The public `Streams` properties should become `EventFlows`:

```text
IEventCoordinator.Streams -> IEventCoordinator.EventFlows
IEventBus.Streams         -> IEventBus.EventFlows
Event.StreamId            -> Event.EventFlowId
```

Do not keep compatibility aliases. The old `stream` terminology is retired from the interruptible live-flow API.

The docs must clearly define:

```text
Event flow: interruptible live event group
Replay source: historical or synthetic event input
Replay timeline: deterministic ordered replay of one or more sources
```

Because breaking changes are acceptable for the HPD architecture, this proposal includes the rename as part of the replay implementation.

---

## 8. Relationship To Event Coordinator

Replay must not be added to `IEventCoordinator`.

`IEventCoordinator` already has a large runtime surface:

- emit
- subscribe
- owned inbox creation
- struct event emit
- struct subscriptions
- request/response
- parent hierarchy
- interruptible event flows
- stats

Adding replay there would make the coordinator both a live delivery object and a historical input object.

Instead, replay publishes into the existing narrow surface:

```csharp
await replay.PublishAsync(publisher, ReplayReadOptions.All, ct);
```

This keeps dependencies honest:

- replay sources do not need a bus
- replay publishers need only `IEventPublisher`
- deterministic consumers can read directly without any bus
- live systems can keep using `IEventInboxSource`

---

## 9. Rhodium Integration

Rhodium should consume HPD replay without changing HPD replay's domain model.

### 9.1 ReplayConnector

`ReplayConnector` already accepts:

```csharp
IAsyncEnumerable<FinanceEvent> history
```

It can consume a replay timeline directly:

```csharp
var replay = ReplayTimeline<FinanceEvent>.Create()
    .AddSource("quotes", quoteSource)
    .AddSource("trades", tradeSource)
    .AddSource("books", bookSource)
    .WithOrdering(FinanceReplayOrderingPolicy.Default);

var connector = new ReplayConnector(
    replay.ReadAsync(ReplayReadOptions.All));
```

### 9.2 SimulationRuntime

`SimulationRuntime` can materialize the same replay stream:

```csharp
var history = await SharedHistory.LoadAsync(
    replay.ReadAsync(ReplayReadOptions.All, ct),
    ct);
```

### 9.3 FinanceReplayOrderingPolicy

Rhodium owns finance-specific tie rules:

```csharp
public sealed class FinanceReplayOrderingPolicy
    : IReplayOrderingPolicy<FinanceEvent>
{
    public static FinanceReplayOrderingPolicy Default { get; }
}
```

This keeps HPD Events reusable and lets Rhodium be precise.

---

## 10. Example Usage

### 10.1 In-Memory Replay

```csharp
var source = new EnumerableReplaySource<Event>(events);

var replay = ReplayTimeline<Event>.Create()
    .AddSource("fixture", source);

await foreach (var evt in replay.ReadAsync(ReplayReadOptions.All, ct))
{
    Process(evt);
}
```

### 10.2 Multi-Source Replay

```csharp
var replay = ReplayTimeline<Event>.Create()
    .AddSource("control", controlEvents, priority: 0)
    .AddSource("telemetry", telemetryEvents, priority: 10)
    .AddSource("audit", auditEvents, priority: 20);

await replay.PublishAsync(eventBus, ReplayReadOptions.All, ct);
```

### 10.3 Event Store Replay

```csharp
IEventStore<Event> store = new InMemoryEventStore<Event>();

await store.AppendAsync(started, ct);
await store.AppendAsync(completed, ct);

var replay = ReplayTimeline<Event>.Create()
    .AddSource("store", store);

await replay.PublishAsync(events, ReplayReadOptions.All, ct);
```

---

## 11. Testing Plan

Use the existing `dotnet/shared/test/HPD-Events.Tests` project.

### 11.1 ReplayTimeline Tests

Required tests:

- single source preserves source order
- empty source completes
- empty multi-source timeline completes
- multiple sources merge by timestamp
- same timestamp uses source priority
- same timestamp and source priority uses event priority
- same timestamp, priority, and event priority uses source ordinal
- same timestamp, priority, event priority, and source ordinal uses source sequence
- `ExchangeTimestampNs` beats `Timestamp`
- `Timestamp` fallback works when `ExchangeTimestampNs` is zero
- `From` is inclusive
- `To` is exclusive
- `EventFlowId` filters by event-flow id
- `Limit` caps total output
- cancellation stops replay

### 11.2 EventStore Tests

Required tests:

- appended events are readable
- append order is preserved for same-key events
- read options filter appended events
- cancellation is respected

### 11.3 ReplayPublisher Tests

Required tests:

- publishes all replayed events into `EventBus`
- published events can be consumed through `CreateInbox<TEvent>`
- publish respects cancellation
- publish does not require `IEventCoordinator`
- publish uses only `IEventPublisher`

### 11.4 Clock Tests

Required tests:

- `SystemClock.UtcNow` is UTC
- `SystemClock.UnixNanos` is positive and monotonic enough for wall-clock use
- `ManualClock.Set` changes current time
- `ManualClock.Advance` moves time forward deterministically
- manual one-shot alert fires when advanced past alert time
- manual recurring timer fires deterministic counts when advanced

---

## 12. Implementation Plan

### Phase 1: Event-Flow Naming Cleanup

Rename interruptible stream APIs before adding replay, so replay does not land on ambiguous terminology:

```text
IStreamRegistry -> IEventFlowRegistry
IStreamHandle   -> IEventFlowHandle
StreamRegistry  -> EventFlowRegistry
StreamHandle    -> EventFlowHandle
Streams         -> EventFlows
Event.StreamId  -> Event.EventFlowId
```

Gate:

- no compatibility aliases remain for the old stream registry API
- docs no longer use "stream" ambiguously
- existing stream interruption behavior remains covered
- project builds with the new event-flow names

### Phase 2: Replay Abstractions

Create:

```text
Abstractions/IReplaySource.cs
Abstractions/IEventStore.cs
Abstractions/IReplayOrderingPolicy.cs
Abstractions/ReplayKey.cs
Abstractions/ReplayReadOptions.cs
Abstractions/ReplaySourceInfo.cs
```

Gate:

- project builds
- no external dependencies
- public XML docs exist for all new public types

### Phase 3: Built-In Sources And Store

Create:

```text
Core/EnumerableReplaySource.cs
Core/AsyncEnumerableReplaySource.cs
Core/InMemoryEventStore.cs
Core/DefaultReplayOrderingPolicy.cs
```

Gate:

- source/store tests pass
- read option filtering works
- cancellation tests pass

### Phase 4: ReplayTimeline

Create:

```text
Core/ReplayTimeline.cs
```

Gate:

- deterministic merge tests pass
- same-time tie tests pass
- no reliance on task completion ordering

### Phase 5: Replay Publishing

Add `ReplayTimeline<TEvent>.PublishAsync`.

Gate:

- publisher tests pass
- event bus inbox receives replay events in timeline order
- replay does not require callback subscriptions

### Phase 6: Clocks

Create:

```text
Core/SystemClock.cs
Core/ManualClock.cs
```

Gate:

- clock tests pass
- Rhodium can use `SystemClock` instead of owning a duplicate production clock

---

## 13. Files To Add

### HPD Events

| File | Purpose |
|------|---------|
| `Abstractions/IReplaySource.cs` | Universal replay input |
| `Abstractions/IEventStore.cs` | Durable append/read replay source |
| `Abstractions/IReplayOrderingPolicy.cs` | Deterministic replay key policy |
| `Abstractions/ReplayKey.cs` | Lexicographic replay ordering key |
| `Abstractions/ReplayReadOptions.cs` | Read filters and limits |
| `Abstractions/ReplaySourceInfo.cs` | Source identity, priority, and ordinal |
| `Abstractions/IEventFlowRegistry.cs` | Interruptible event-flow registry |
| `Abstractions/IEventFlowHandle.cs` | Interruptible event-flow handle |
| `Core/EnumerableReplaySource.cs` | In-memory enumerable source |
| `Core/AsyncEnumerableReplaySource.cs` | Async enumerable adapter |
| `Core/InMemoryEventStore.cs` | Process-local event store |
| `Core/DefaultReplayOrderingPolicy.cs` | Generic HPD event ordering |
| `Core/ReplayTimeline.cs` | Multi-source deterministic replay |
| `Core/SystemClock.cs` | Production `IClock` |
| `Core/ManualClock.cs` | Deterministic test/replay clock |
| `Core/EventFlowRegistry.cs` | Interruptible event-flow registry implementation |
| `Core/EventFlowHandle.cs` | Interruptible event-flow handle implementation |

### Tests

| File | Purpose |
|------|---------|
| `HPD-Events.Tests/ReplayTimelineTests.cs` | Timeline merge tests |
| `HPD-Events.Tests/ReplaySourceTests.cs` | Source and store tests |
| `HPD-Events.Tests/ReplayPublisherTests.cs` | Bus publish tests |
| `HPD-Events.Tests/ClockTests.cs` | Clock behavior tests |
| `HPD-Events.Tests/EventFlowRegistryTests.cs` | Renamed event-flow interruption behavior |

---

## 14. Files To Update

| File | Change |
|------|--------|
| `HPD-Events.csproj` | Include new replay source files automatically through SDK globbing |
| `Event.cs` | Rename `StreamId` to `EventFlowId` |
| `IEventCoordinator.cs` | Rename stream registry surface to event-flow registry |
| `IEventBus.cs` | Rename stream registry surface to event-flow registry |
| `IStreamRegistry.cs` | Replace with `IEventFlowRegistry.cs` |
| `IStreamHandle.cs` | Replace with `IEventFlowHandle.cs` |
| `EventCoordinator.cs` | Wire renamed event-flow registry |
| `EventBus.cs` | Wire renamed event-flow registry |
| `EventChannelRouter.cs` | Use renamed event-flow registry |
| `StreamRegistry.cs` | Replace with `EventFlowRegistry.cs` |
| `StreamHandle.cs` | Replace with `EventFlowHandle.cs` |
| `HPD.Events.BusInboxArchitecture.Proposal.md` | Cross-reference replay timeline proposal |

---

## 15. Non-Goals

This proposal does not add:

- finance-specific event ordering
- file format readers
- database-backed event stores
- distributed event storage
- event-sourcing aggregate APIs
- exactly-once distributed replay semantics
- automatic sorting of arbitrarily unordered sources
- wall-clock paced playback

Those can be layered later.

The first goal is deterministic ordered replay, not a storage platform.

---

## 16. Acceptance Criteria

The proposal is implemented when:

1. `HPD.Events` exposes `IReplaySource<TEvent>`, `IEventStore<TEvent>`, `IReplayOrderingPolicy<TEvent>`, and `ReplayTimeline<TEvent>`.
2. Multiple replay sources can be merged deterministically.
3. Same-time ties are resolved by explicit replay key fields.
4. Replay can be read directly as `IAsyncEnumerable<TEvent>`.
5. Replay can publish into `IEventPublisher`.
6. Tests prove timestamp, source priority, event priority, source ordinal, source sequence, and event sequence ordering.
7. Tests prove read filters and cancellation.
8. `SystemClock` and `ManualClock` are available from `HPD.Events`.
9. HPD Events remains domain-agnostic and dependency-free.
10. Rhodium can consume a `ReplayTimeline<FinanceEvent>` without adding replay merge logic to `ReplayConnector`.
11. Interruptible live-event APIs use event-flow terminology, including `Event.EventFlowId`, with no old stream registry aliases.

---

## 17. Final Decision

Add replay to `HPD.Events` as a first-class, domain-agnostic replay timeline layer.

Do not add replay to `IEventCoordinator`.

Do not make HPD Events finance-aware.

Do not force every replay input to be a durable event store.

The official public model is:

```text
IReplaySource<TEvent>
    -> ReplayTimeline<TEvent>
    -> ReadAsync(...) or PublishAsync(...)
```

`IEventStore<TEvent>` is included as the durable append/read specialization, but `ReplayTimeline<TEvent>` is the central user experience.
