# Engineering Proposal: HPD Events Local Struct Event Lane

**Document:** `HPD.Events.LocalStructEventLane.Proposal.md`  
**Version:** 1.0.0  
**Date:** May 2026  
**Status:** Implemented initial transition  
**Target:** `HPD.Events`  
**Compatibility Stance:** Breaking changes are acceptable. Prefer clean architecture over retrofits.  
**Constraint:** Domain-agnostic, process-local, AOT/trim compatible, zero external package dependencies.

---

## Executive Summary

HPD Events has two fundamentally different event families:

1. **Class events**: semantic HPD workflow events with routing channels, bubbling, request/response, event-flow interruption, replay, and observer/inbox semantics.
2. **Struct events**: process-local hot-path frames for high-volume systems that need typed, bounded, low-allocation event movement.

The current struct-event path is a useful first implementation, but it is shaped like a smaller version of the class-event bus. That is not the best long-term design.

This proposal replaces the current struct subsystem with a clean, route-based local struct event lane:

```text
LocalStructEventBus
    -> LocalStructEventRoute<TEvent>
        -> LocalStructEmitter<TEvent>
        -> LocalStructInbox<TEvent>
        -> local observers
        -> route stats
```

The struct lane is not a semantic workflow bus. It does not bubble to parents. It does not participate in request/response. It does not use event-flow interruption. It does not know about Rhodium, agents, graph runtimes, finance, audio, or any domain.

The struct lane core is synchronous. It does not expose async readers, channel readers, or async disposal. Async adapters may be built outside HPD Events core, but they are not part of the no-allocation hot-path contract.

It provides generic infrastructure that high-volume HPD projects can build on:

- typed process-local routes
- copy-on-write subscriber arrays
- allocation-free steady-state emit
- bounded ring-buffer inboxes
- explicit emit results
- batch-first APIs
- non-boxing sequenced emitters
- observable drop/backpressure stats

---

## 1. Motivation

### 1.1 The Current Struct Path Is Not Hot Enough

The current struct router routes by exact event type and uses bounded channels. That part is good.

However, in steady-state emit it still:

- looks up subscribers by `typeof(TEvent)`
- locks a subscriber list
- allocates a `ToArray()` snapshot
- returns only `bool`
- tracks no struct-specific stats
- sequences through a runtime interface check

Those costs are acceptable for ordinary observer-style events. They are not appropriate for a hot-path struct lane that may carry market frames, telemetry samples, audio frames, model-training measurements, or runtime scheduling frames.

### 1.2 HPD Events Should Stay Domain-Neutral

This proposal is not about Rhodium. Rhodium is only an example of a project that would benefit from better local struct lanes.

HPD Events must not learn about:

- instruments
- quotes
- trades
- order books
- strategies
- graph nodes
- agents
- audio chunks
- model tensors

HPD Events should provide the transport and ownership semantics. Domain packages define their own struct frame shapes.

### 1.3 No Retrofit Bias

Backward compatibility is not a goal for this proposal.

If old names are misleading, replace them. If an interface was shaped around an early implementation, replace the interface. If a convenience method hides important semantics, remove it or demote it to an extension later.

The goal is a clean substrate, not a compatibility-preserving patch.

---

## 2. Design Principles

### 2.1 Struct Events Are Local Frames

Struct events are process-local hot-path frames. They are not semantic workflow events.

They should be used for:

- high-volume samples
- real-time frames
- local telemetry
- simulation ticks
- engine-internal state changes
- compact typed messages between in-process components

They should not be used when the event needs:

- parent bubbling
- cross-process serialization by default
- request/response correlation
- interruptible event-flow semantics
- durable semantic replay through the class-event timeline

### 2.2 Routes Are First-Class

The struct subsystem should be route-based, not lookup-based.

A route is the typed local lane for one struct event type:

```csharp
LocalStructEventRoute<MyFrame>
```

Emitters, inboxes, observers, stats, and batch operations are all bound to that route.

### 2.3 Emit Is Allocation-Free In Steady State

Subscription and disposal are cold-path operations. They may lock and allocate.

Emit is hot path. Once routes are built and subscribers are attached, emit should not:

- allocate subscriber snapshots
- lock subscriber lists
- use reflection
- box struct events
- enumerate dictionaries

### 2.4 Batch Is A First-Class Operation

Single event emit remains useful, but high-volume systems usually move frames in batches.

The API should make batch emit and batch read natural, not an afterthought.

### 2.5 Results Must Be Explicit

`bool` is too poor for hot-path diagnostics.

A producer needs to know whether an event was:

- accepted
- dropped
- filtered
- rejected because there were no subscribers
- rejected because the route was disposed

This must be observable without requiring logging or debugging.

---

## 3. Non-Goals

This proposal does not add:

- finance-specific event types
- domain-specific union frames
- async struct-event readers
- class-event parent bubbling for struct events
- request/response for struct events
- event-flow interruption for struct events
- distributed transport
- serialization contracts
- durable storage
- wall-clock paced playback
- a replacement for class `Event`

Struct replay is considered later work and should be separate from class replay.

---

## 4. Proposed Public API

Names are intentionally explicit. `LocalStruct` means process-local struct event infrastructure.

### 4.1 Event Contract

Keep the existing core contract, with clearer documentation:

```csharp
public interface IStructEvent
{
    EventKind Kind { get; }

    long SequenceNumber { get; }

    long TimestampNs { get; }
}
```

`Kind` remains useful for generic observability. `TimestampNs` and `SequenceNumber` support generic ordering and diagnostics.

### 4.2 Sequencing Contract

Keep the copy-style contract:

```csharp
public interface ISequencedStructEvent<TSelf>
    where TSelf : struct, IStructEvent
{
    TSelf WithSequenceNumber(long sequenceNumber);
}
```

Do not use runtime interface checks in the hot path. Sequenced emitters use constrained generics.

### 4.3 Bus Contract

The struct bus contract is:

```csharp
public interface ILocalStructEventBus
{
    LocalStructEventRoute<TEvent> Route<TEvent>()
        where TEvent : struct, IStructEvent;

    LocalStructEventBusStats GetStats();

    IReadOnlyList<LocalStructEventTypeStats> GetRouteStats();
}
```

The bus is a route registry and stats surface. Hot-path operations happen through typed routes and emitters.

### 4.4 Route

```csharp
public sealed class LocalStructEventRoute<TEvent>
    where TEvent : struct, IStructEvent
{
    public LocalStructEmitter<TEvent> CreateEmitter(
        LocalStructEmitterOptions<TEvent>? options = null);

    public LocalStructInbox<TEvent> CreateInbox(
        LocalStructInboxOptions? options = null);

    public LocalStructSubscription<TEvent> Subscribe(
        LocalStructSubscriptionOptions? options = null);

    public IDisposable Observe(
        Func<TEvent, ValueTask> handler,
        LocalStructSubscriptionOptions? options = null);

    public LocalStructEventTypeStats GetStats();
}
```

Routes own:

- subscriber arrays
- route counters
- route sequence counter
- subscriber lifecycle

Sequenced emitters are exposed as a constrained extension method. C# cannot add a stronger constraint to a route's existing `TEvent` on an instance method, so the extension method preserves the desired DX while keeping the type constraint honest:

```csharp
public static LocalSequencedStructEmitter<TEvent> CreateSequencedEmitter<TEvent>(
    this LocalStructEventRoute<TEvent> route,
    LocalStructEmitterOptions<TEvent>? options = null)
    where TEvent : struct, IStructEvent, ISequencedStructEvent<TEvent>;
```

### 4.5 Emitters

Unsequenced emitter:

```csharp
public readonly struct LocalStructEmitter<TEvent>
    where TEvent : struct, IStructEvent
{
    public LocalStructEmitResult Emit(in TEvent evt);

    public LocalStructEmitBatchResult EmitBatch(
        ReadOnlySpan<TEvent> events);
}
```

Sequenced emitter:

```csharp
public readonly struct LocalSequencedStructEmitter<TEvent>
    where TEvent : struct, IStructEvent, ISequencedStructEvent<TEvent>
{
    public LocalStructEmitResult Emit(in TEvent evt);

    public LocalStructEmitBatchResult EmitBatch(
        ReadOnlySpan<TEvent> events);
}
```

The sequenced emitter assigns sequence numbers without boxing.

### 4.6 Inboxes And Subscriptions

Owned inbox:

```csharp
public readonly struct LocalStructInbox<TEvent> : IDisposable
    where TEvent : struct, IStructEvent
{
    public bool TryRead(out TEvent evt);

    public int TryReadBatch(Span<TEvent> destination);
}
```

Subscription:

```csharp
public readonly struct LocalStructSubscription<TEvent> : IDisposable
    where TEvent : struct, IStructEvent
{
    public bool TryRead(out TEvent evt);

    public int TryReadBatch(Span<TEvent> destination);
}
```

The distinction remains:

- inboxes are caller-owned deterministic lanes
- subscriptions are direct reader subscriptions
- observers are callback pumps

If the implementation can collapse inbox and subscription internally, that is fine. The public model should remain clear.

`TryRead` and `TryReadBatch` are the core APIs. There is intentionally no async reader surface in the local struct lane. Disposal is synchronous. Code that wants async enumeration can build an adapter outside the hot-path subsystem, with its own allocation and scheduling contract.

### 4.7 Emit Result

```csharp
public enum LocalStructEmitStatus
{
    Accepted,
    NoSubscribers,
    Filtered,
    Dropped,
    Backpressured,
    Rejected,
    Disposed
}
```

```csharp
public readonly record struct LocalStructEmitResult(
    LocalStructEmitStatus Status,
    int SubscriberCount,
    int AcceptedCount,
    int DroppedCount)
{
    public bool Accepted => AcceptedCount > 0;
}
```

For fan-out, a single event may be accepted by some subscribers and dropped by others. `Status` represents the aggregate outcome:

- `Accepted`: at least one subscriber accepted the event
- `NoSubscribers`: the route had no subscribers
- `Filtered`: emitter filter rejected the event before routing
- `Dropped`: subscribers existed, but none accepted the event
- `Backpressured`: a non-lossy subscriber could not accept the event immediately
- `Rejected`: a reject-mode subscriber refused the event because it was full
- `Disposed`: route or bus was disposed

### 4.8 Batch Result

```csharp
public readonly record struct LocalStructEmitBatchResult(
    int EventCount,
    int AcceptedEvents,
    int DroppedEvents,
    int BackpressuredEvents,
    int RejectedEvents,
    int FilteredEvents,
    int TotalSubscriberWrites,
    int TotalSubscriberDrops);
```

Batch result counts event-level outcomes and subscriber-level fan-out outcomes.

### 4.9 Options

Emitter options:

```csharp
public sealed record LocalStructEmitterOptions<TEvent>
    where TEvent : struct, IStructEvent
{
    public Func<TEvent, bool>? Filter { get; init; }
}
```

Inbox options:

```csharp
public sealed record LocalStructInboxOptions
{
    public int Capacity { get; init; } = 1024;

    public LocalStructFullMode FullMode { get; init; } =
        LocalStructFullMode.Backpressure;
}
```

Subscription options:

```csharp
public sealed record LocalStructSubscriptionOptions
{
    public int Capacity { get; init; } = 1024;

    public LocalStructFullMode FullMode { get; init; } =
        LocalStructFullMode.DropOldest;
}
```

Overflow mode:

```csharp
public enum LocalStructFullMode
{
    Backpressure,
    DropOldest,
    DropNewest,
    Reject
}
```

The local struct lane has exactly one built-in storage backend: a bounded ring buffer. This avoids splitting the semantics between a hot-path implementation and an ergonomic channel implementation.

---

## 5. Internal Architecture

### 5.1 Route Registry

The bus owns a route registry:

```csharp
private readonly ConcurrentDictionary<Type, object> _routes = new();
```

The dictionary lookup happens when `Route<TEvent>()` is called, not on every emit.

```csharp
public LocalStructEventRoute<TEvent> Route<TEvent>()
    where TEvent : struct, IStructEvent =>
    (LocalStructEventRoute<TEvent>)_routes.GetOrAdd(
        typeof(TEvent),
        static _ => new LocalStructEventRoute<TEvent>());
```

Users that care about the hot path should cache the route or emitter:

```csharp
var emitter = events.Route<MyFrame>().CreateEmitter();
```

### 5.2 Copy-On-Write Subscribers

Each route maintains a subscriber array:

```csharp
private readonly object _subscriberGate = new();
private volatile LocalStructSubscriber<TEvent>[] _subscribers = [];
```

Subscribe:

```csharp
lock (_subscriberGate)
{
    var next = new LocalStructSubscriber<TEvent>[_subscribers.Length + 1];
    _subscribers.CopyTo(next, 0);
    next[^1] = subscriber;
    Volatile.Write(ref _subscribers, next);
}
```

Dispose:

```csharp
lock (_subscriberGate)
{
    var current = _subscribers;
    var next = current.Where(s => s.Id != subscriberId).ToArray();
    Volatile.Write(ref _subscribers, next);
}
```

Emit:

```csharp
var subscribers = Volatile.Read(ref _subscribers);
for (var i = 0; i < subscribers.Length; i++)
{
    subscribers[i].TryWrite(evt);
}
```

Steady-state emit has no route lock and no subscriber snapshot allocation.

### 5.3 Subscriber Backend

The local struct lane uses bounded ring buffers.

Each subscriber owns a bounded ring:

```csharp
private TEvent[] _buffer;
private int _head;
private int _tail;
private int _count;
```

The first implementation should support:

- SPSC ring for the common one-producer/one-consumer lane
- MPSC ring for fan-in lanes where multiple producers emit to the same subscriber
- `TryWrite`
- `TryRead`
- `TryReadBatch`
- `DropOldest`
- `DropNewest`
- `Reject`
- `Backpressure` as a returned status, not an implicit async wait

`Backpressure` means "the subscriber could not accept this event now." It should not park the producer, allocate a waiter, or call into any async scheduling primitive from the hot path.

### 5.4 What Removing Async Reader Support Costs

The local struct lane intentionally removes built-in async reader integration.

That means HPD Events core will not provide:

- an async enumerable reader over struct events
- producer parking when a subscriber is full
- built-in waiter registration for empty inboxes
- scheduler handoff from emit to reader
- direct compatibility with consumers that expect a framework-provided async queue

This is a deliberate loss.

The replacement contract is simpler and more honest:

- producers call `Emit` or `EmitBatch`
- consumers call `TryRead` or `TryReadBatch`
- full subscribers return `Backpressured`, `Dropped`, or `Rejected` depending on policy
- empty subscribers return `false` or `0`
- sleeping, waiting, yielding, scheduling, and async adaptation belong outside the core lane

This keeps HPD Events responsible for the thing it can make strict: local typed struct routing over bounded memory. Higher-level projects can still build async adapters around inboxes, but those adapters are not part of the no-allocation guarantee.

### 5.5 Allocation Closure

The proposal divides allocation behavior into hot-path and cold-path categories.

Steady-state no-allocation target:

- cached route
- cached emitter
- ring-buffer subscribers
- static/no-capture filter or no filter
- synchronous `Emit`
- synchronous `EmitBatch`
- `TryRead`
- `TryReadBatch`

Cold-path allocations are acceptable:

- route creation
- emitter creation
- inbox/subscription creation
- observer pump creation
- subscribe/unsubscribe copy-on-write arrays
- class-event adapter creation in domain packages

The no-allocation claim is therefore precise:

> Cached synchronous emit and drain over ring-buffer subscribers must be allocation-free in steady state.

Observer callback pumps are not part of that claim. They are convenience observers, not the primary hot-path lane.

### 5.6 Route Counters

Each route tracks:

- emitted events
- accepted events
- dropped events
- filtered events
- subscriber write count
- subscriber drop count
- current queued depth
- max observed queued depth
- subscriber count
- inbox count
- observer count

Use `long` counters internally.

### 5.7 Depth Tracking

The current class-event router tracks depth. The struct route should do the same.

On successful write:

```csharp
Interlocked.Increment(ref _depth);
```

On read:

```csharp
DecrementDepth();
```

For `TryReadBatch`, decrement by the number of events read.

For ring-buffer drops, decrement appropriately and increment drop counters.

### 5.8 Disposal

Disposing a route should:

- atomically mark disposed
- complete all subscriber writers
- replace subscriber array with empty
- prevent new inboxes/subscriptions/observers
- return `Disposed` on emit

Disposing the bus should dispose all routes.

---

## 6. Observability

### 6.1 Type Stats

```csharp
public readonly record struct LocalStructEventTypeStats(
    Type EventType,
    int SubscriberCount,
    int InboxCount,
    int ObserverCount,
    int CurrentQueued,
    int MaxQueued,
    long Emitted,
    long Accepted,
    long Dropped,
    long Filtered,
    long SubscriberWrites,
    long SubscriberDrops);
```

### 6.2 Bus Stats

```csharp
public readonly record struct LocalStructEventBusStats(
    int RouteCount,
    int SubscriberCount,
    int InboxCount,
    int ObserverCount,
    int CurrentQueued,
    int MaxQueued,
    long Emitted,
    long Accepted,
    long Dropped,
    long Filtered,
    long SubscriberWrites,
    long SubscriberDrops);
```

### 6.3 No Diagnostic Class Events By Default

The struct lane should not emit class diagnostic events by default. That would contaminate the hot path.

If users want diagnostics, they can poll stats or attach a local observer.

---

## 7. Relationship To Existing Class Events

Class events remain the semantic HPD event model.

Class events support:

- `IEventPublisher`
- `IEventObserverBus`
- `IEventInboxSource`
- `IRequestResponseBus`
- `IHierarchicalEventBus`
- `IEventFlowRegistry`
- `ReplayTimeline<TEvent> where TEvent : Event`

Local struct events support:

- typed local routes
- typed emitters
- typed local inboxes
- typed local observers
- route stats

There is intentionally no automatic bridge between the two.

If a domain wants to convert struct frames into semantic class events, that adapter belongs in the domain package.

---

## 8. Relationship To Replay

Current replay is class-event replay:

```csharp
ReplayTimeline<TEvent> where TEvent : Event
```

Do not force struct replay into that model.

If needed later, add:

```csharp
public interface ILocalStructReplaySource<TEvent>
    where TEvent : struct, IStructEvent
{
    IAsyncEnumerable<TEvent> ReadAsync(
        LocalStructReplayReadOptions options,
        CancellationToken ct = default);
}
```

and:

```csharp
public sealed class LocalStructReplayTimeline<TEvent>
    where TEvent : struct, IStructEvent
```

That is intentionally future work.

---

## 9. Migration Plan

Compatibility shims are not required.

### Phase 1: Add New Local Struct API

Add:

```text
Abstractions/ILocalStructEventBus.cs
Abstractions/LocalStructEmitResult.cs
Abstractions/LocalStructEmitBatchResult.cs
Abstractions/LocalStructEventStats.cs
Abstractions/LocalStructOptions.cs
Abstractions/LocalStructInbox.cs
Abstractions/LocalStructSubscription.cs
Core/LocalStructEventBus.cs
Core/LocalStructEventRoute.cs
Core/LocalStructEmitter.cs
Core/LocalStructRingBuffer.cs
```

Gate:

- project builds
- no external dependencies
- public XML docs exist

### Phase 2: Replace Old Struct Surface

Remove the legacy struct bus/router/emitter/inbox/subscription surface and the coordinator-level struct convenience methods. The route-based local struct lane is the only struct event API.

Keep:

```text
IStructEvent
ISequencedStructEvent<TSelf>
```

Gate:

- no old struct surface remains
- tests use route-based local struct API

### Phase 3: Update EventBus/EventCoordinator Composition

Recommended outcome:

- `EventBus` implements class-event interfaces.
- `EventBus` may expose `ILocalStructEventBus` through a property, or a separate `LocalStructEventBus` is registered/composed by hosts.

Preferred clean model:

```csharp
public sealed class EventBus : IEventBus, IRequestResponseBus, IHierarchicalEventBus
{
    public ILocalStructEventBus LocalStructs { get; }
}
```

This keeps class-event semantics and struct-lane semantics visibly separate.

### Phase 4: Tests

Add tests for:

- route caching returns same route for same type
- emitter emits with no subscribers and returns `NoSubscribers`
- emitter emits to all subscribers
- emit does not allocate subscriber snapshots in steady state
- cached emit to ring-buffer subscribers allocates zero bytes in a benchmark test
- cached batch emit to ring-buffer subscribers allocates zero bytes in a benchmark test
- batch drain from ring-buffer inbox allocates zero bytes in a benchmark test
- subscribe/dispose uses copy-on-write route array
- route stats update on emit/read/drop
- batch emit reports aggregate counts
- batch read drains available items
- `Backpressure` returns a result instead of parking the producer
- reject-mode full subscribers return `Rejected`
- sequenced emitter assigns sequence numbers without runtime interface checks
- local struct events do not bubble into class parent buses
- class event subscribers do not receive local struct events
- disposed route returns `Disposed`
- observer handler faults remove only that observer

### Phase 5: Static Guard

Keep a source guard in `HPD.Events.Tests` that fails if the legacy struct bus/router/emitter/inbox/subscription API names, including the old option type names, reappear in production `HPD.Events` source.

Do not guard against `IStructEvent` or `ISequencedStructEvent<TSelf>`.

---

## 10. Example Usage

### 10.1 Define A Domain Frame

```csharp
public readonly record struct SampleFrame(
    int SourceId,
    double Value,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<SampleFrame>
{
    public EventKind Kind => EventKind.Content;

    public SampleFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}
```

### 10.2 Emit Frames

```csharp
var route = localStructs.Route<SampleFrame>();
var emitter = route.CreateSequencedEmitter();

var result = emitter.Emit(new SampleFrame(
    SourceId: 7,
    Value: 42.0,
    TimestampNs: clock.UnixNanos));

if (!result.Accepted)
{
    // Producer can decide whether no subscribers or drops matter.
}
```

### 10.3 Own A Deterministic Reader

```csharp
using var inbox = localStructs
    .Route<SampleFrame>()
    .CreateInbox(new LocalStructInboxOptions
    {
        Capacity = 8192,
        FullMode = LocalStructFullMode.Backpressure
    });

var buffer = new SampleFrame[256];

while (!ct.IsCancellationRequested)
{
    var count = inbox.TryReadBatch(buffer);
    if (count == 0)
    {
        Thread.Yield();
        continue;
    }

    for (var i = 0; i < count; i++)
    {
        Process(buffer[i]);
    }
}
```

This is the intended form: explicit polling or draining over a bounded local ring. Code that prefers ordinary async waiting should build that adapter outside HPD Events core and accept that it is outside the strict no-allocation target.

### 10.4 Observe Without Owning The Lane

```csharp
using var observer = localStructs
    .Route<SampleFrame>()
    .Observe(frame =>
    {
        metrics.Record(frame.Value);
        return ValueTask.CompletedTask;
    });
```

---

## 11. Acceptance Criteria

The proposal is implemented when:

1. Struct events are exposed through a local route-based API.
2. Hot-path emit does not lock subscriber lists.
3. Hot-path emit does not allocate subscriber snapshots.
4. Pre-bound emitters do not perform per-event type dictionary lookup.
5. Sequenced emitters assign sequence numbers without boxing.
6. Emit results distinguish accepted, dropped, filtered, no subscribers, and disposed.
7. Batch emit and batch read APIs exist.
8. Ring-buffer inboxes are the only built-in struct-lane storage backend.
9. Cached synchronous emit/drain over ring-buffer subscribers is allocation-free in benchmark tests.
10. Per-route and aggregate stats exist.
11. Struct events remain process-local and do not bubble into class-event parents.
12. Class-event observers do not receive struct events.
13. HPD Events remains domain-agnostic.
14. Old struct bus APIs are removed rather than shimmed.

---

## 12. Final Decision

Replace the current struct subsystem with a clean local struct event lane.

Do not retrofit the existing API.

Do not make HPD Events aware of any domain.

The official model is:

```text
ILocalStructEventBus
    -> LocalStructEventRoute<TEvent>
        -> LocalStructEmitter<TEvent>
        -> LocalSequencedStructEmitter<TEvent>
        -> LocalStructInbox<TEvent>
        -> LocalStructSubscription<TEvent>
        -> LocalStructEventTypeStats
```

This gives HPD projects a reusable, domain-neutral hot-path event substrate without confusing it with semantic class-event delivery.
