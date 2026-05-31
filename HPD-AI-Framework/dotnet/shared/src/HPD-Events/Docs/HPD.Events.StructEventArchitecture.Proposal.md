# Engineering Proposal: Split HPD Events Into EventBus And StructEventHub

**Document:** `HPD.Events.StructEventArchitecture.Proposal.md`  
**Version:** 1.0.0  
**Date:** May 2026  
**Status:** Proposal  
**Target:** `HPD.Events`  
**Compatibility stance:** Breaking changes are preferred. No compatibility shims. No retrofits.  
**Constraint:** Domain-agnostic, Native AOT compatible, zero external package dependencies in core surfaces.

## Executive Summary

HPD Events should be rebuilt around two native event families:

```text
Event
    semantic/control/workflow/replay events

StructEvent
    process-local realtime value events
```

The current library blends these concerns. `EventBus` and `EventCoordinator` expose class-event routing, event-flow interruption, request/response, replay-compatible event shape, and a `LocalStructs` property for high-volume struct lanes. That blend is convenient, but it makes realtime consumers inherit control-plane concepts and makes control-plane consumers see a hot-path subsystem they may not need.

This proposal breaks that blend deliberately.

The official model should become:

```text
EventBus
    semantic class-event bus
    control-plane friendly
    async/channel/replay friendly

StructEventHub
    process-local struct-event lane registry
    synchronous
    bounded
    pull-based
    allocation-free after warmup for cached route/emitter/inbox paths
```

There is no bridge between them in HPD Events. A domain component that needs both emits both intentionally. HPD Events should not automatically mirror class events into struct events, struct events into class events, or replay struct events through class-event replay.

The existing local struct lane is a good prototype. Keep the core ideas:

- typed routes
- cached emitters
- struct constraints
- bounded ring buffers
- batch emit and batch read
- explicit drop/backpressure results
- optional sequencing
- stats

But change the public architecture now, before other packages depend on it.

## 1. Problem Statement

HPD Events currently tries to serve two very different uses:

1. Semantic event coordination.
2. Process-local high-volume struct event movement.

Those are both valid, but they have different rules.

Semantic events can tolerate:

- class allocation
- async pumps
- channels
- callback subscriptions
- request/response waiters
- event-flow interruption
- replay timelines
- richer lifecycle and diagnostic metadata

Struct events need a much stricter contract:

- `struct` event values
- synchronous emit
- synchronous pull/read
- bounded memory
- explicit overflow behavior
- no class event allocation
- no async state machine in the lane
- no `Channel<T>`
- no automatic replay
- no object metadata
- no hidden bridge to `EventBus`

The current implementation contains the right pieces, but the public model still communicates "one coordinator owns all event things." That is the wrong long-term shape.

## 2. Design Principles

### 2.1 Break Now

No one depends on this library yet. That is a gift. Do not preserve awkward names, mixed surfaces, or compatibility aliases.

Prefer a clean architecture over an incremental retrofit.

### 2.2 Two Event Families, Not One Hierarchy

`Event` and `StructEvent` are sibling concepts, not parent/child concepts.

```text
Event is not the base type for StructEvent.
StructEvent is not a faster Event.
EventBus does not own StructEventHub.
StructEventHub does not publish Event.
```

### 2.3 No Bridge In Core

HPD Events should not include official adapters or automatic conversion between class events and struct events.

If a WebRTC component wants to emit:

- a semantic fact such as `IceConnectionFailed`
- a local sample such as `IceChecklistProbeSample`

then that component should emit each directly to the correct subsystem.

This keeps meaning explicit and avoids a bridge layer becoming a disguised retrofit.

### 2.4 StructEvent Means Hot-Path Honesty

Keep the name `StructEvent`.

It is not as elegant as alternatives like `LocalEvent` or `Signal`, but it is blunt and useful. It tells consumers the key implementation fact immediately: these values are structs and the API exists for low-allocation local lanes.

The broader semantics should be documented:

- process-local
- bounded
- not durable by default
- not replayed by semantic replay
- not bubbled to parent buses
- not request/response
- not workflow facts

### 2.5 Realtime Guarantees Must Be Tested

It is not enough to say "allocation-free after warmup." HPD Events must prove it with tests and, ideally, benchmarks.

The struct-event layer should have acceptance tests for steady-state managed allocation.

## 3. Proposed Package And Namespace Shape

Preferred physical packages:

```text
HPD.Events
    Event, EventBus, EventInbox, EventFlow, diagnostics

HPD.Events.Struct
    StructEventHub, StructEventRoute<T>, StructEventEmitter<T>, StructEventInbox<T>

HPD.Events.Replay
    ReplayTimeline<TEvent>, IReplaySource<TEvent>, IEventStore<TEvent>

HPD.Events.Testing
    ManualClock, deterministic test helpers
```

If physical packages are too much for the first implementation, use the same split through folders and namespaces in one project:

```text
HPD.Events
HPD.Events.Struct
HPD.Events.Replay
HPD.Events.Testing
```

Hard rule:

```text
HPD.Events.Struct must not depend on EventBus, EventCoordinator, Channel<T>,
ReplayTimeline, request/response, event-flow registry, or async reader surfaces.
```

## 4. Semantic Event Model

`Event` remains the semantic class-event model.

It is for facts such as:

```text
IceConnectionFailed
WebRtcConnectionOpened
SignalingProtocolError
AudioConnectionClosed
AgentTurnInterrupted
GraphNodeCompleted
```

### 4.1 Event Contract

Proposed base:

```csharp
namespace HPD.Events;

public abstract record Event
{
    public virtual EventKind Kind { get; init; } = EventKind.Content;

    public virtual EventChannel Channel { get; init; } = EventChannel.Synchronous;

    public long SequenceNumber { get; init; }

    public long TimestampNs { get; init; }

    public string? EventFlowId { get; init; }

    public bool CanInterrupt { get; init; } = true;
}
```

Changes from current shape:

- Use `TimestampNs` as the primary timestamp field.
- Do not default to `DateTimeOffset.UtcNow` inside the base record.
- Remove `Extensions` from the base event.
- Prefer init-only sequence assignment where possible.

### 4.2 Remove Base Object Metadata

Remove this from base `Event`:

```csharp
IReadOnlyDictionary<string, object>? Extensions
```

Reasons:

- It weakens the AOT and trim story.
- It invites object-bag metadata into places that should use typed fields.
- It makes the base event feel less strict.
- It is not needed for the core event bus contract.

If an escape hatch is required later, add it explicitly:

```csharp
public abstract record ExtensibleEvent : Event
{
    public IEventMetadata? Metadata { get; init; }
}
```

That makes extensibility opt-in rather than foundational.

### 4.3 EventBus Scope

`EventBus` should own semantic class events only:

- publish
- async publish
- typed inboxes
- callback observers
- channel routing
- event-flow registry
- diagnostics
- optional request/response

`EventBus` should not expose:

- `LocalStructs`
- `StructEvents`
- `StructEventHub`
- packet/frame telemetry lanes

Hosts compose `EventBus` and `StructEventHub` when they need both.

## 5. StructEvent Model

`StructEvent` is the process-local realtime value-event family.

It is for high-frequency local values such as:

```text
RtpQueueDepthSample
RtcpJitterSample
AudioPumpCycle
CodecFrameTiming
DatagramDropSample
IceChecklistTick
SrtpReplayRejectSample
```

These are not semantic workflow facts. They are local runtime samples or signals.

### 5.1 Struct Event Contract

Keep the existing core concept:

```csharp
namespace HPD.Events.Struct;

public interface IStructEvent
{
    EventKind Kind { get; }

    long SequenceNumber { get; }

    long TimestampNs { get; }
}
```

Sequencing contract:

```csharp
namespace HPD.Events.Struct;

public interface ISequencedStructEvent<TSelf>
    where TSelf : struct, IStructEvent
{
    TSelf WithSequenceNumber(long sequenceNumber);
}
```

`StructEvent` values must remain structs:

```csharp
where TEvent : struct, IStructEvent
```

`TimestampNs` is producer-supplied. The hub and default emitters must not call
a clock or stamp time implicitly in the hot path. `SequencedStructEventEmitter<T>`
assigns only the route-local sequence number. If timestamp stamping is needed
later, it should be an explicit specialized emitter option with documented cost.

### 5.2 Naming

Replace current public names:

| Current name | Proposed name |
|---|---|
| `LocalStructEventBus` | `StructEventHub` |
| `LocalStructEventRoute<TEvent>` | `StructEventRoute<TEvent>` |
| `LocalStructEmitter<TEvent>` | `StructEventEmitter<TEvent>` |
| `LocalSequencedStructEmitter<TEvent>` | `SequencedStructEventEmitter<TEvent>` |
| `LocalStructInbox<TEvent>` | `StructEventInbox<TEvent>` |
| `LocalStructSubscription<TEvent>` | `StructEventSubscription<TEvent>` |
| `LocalStructFullMode` | `StructEventOverflowMode` |
| `LocalStructEmitResult` | `StructEventEmitResult` |
| `LocalStructEmitBatchResult` | `StructEventEmitBatchResult` |
| `LocalStructEventTypeStats` | `StructEventRouteStats` |
| `LocalStructEventBusStats` | `StructEventHubStats` |

Do not rename `IStructEvent` to `ILocalEvent`. The current name is explicit and acceptable.

## 6. StructEventHub API

### 6.1 Hub

```csharp
namespace HPD.Events.Struct;

public sealed class StructEventHub : IDisposable
{
    public StructEventRoute<TEvent> Route<TEvent>(
        StructEventRouteOptions? options = null)
        where TEvent : struct, IStructEvent;

    public StructEventHubStats GetStats();

    public IReadOnlyList<StructEventRouteStats> GetRouteStats();
}
```

The hub is a route registry and aggregate stats surface. It is not an event bus.

Route creation is first-writer-wins by event type. A later `Route<TEvent>()`
call with no options, or with options equal to the existing route options,
returns the existing route. A later call with non-equivalent options throws
`InvalidOperationException`. Silent option mismatch is not allowed because two
packages may independently cache the same route.

### 6.2 Route

```csharp
namespace HPD.Events.Struct;

public sealed class StructEventRoute<TEvent> : IDisposable
    where TEvent : struct, IStructEvent
{
    public StructEventEmitter<TEvent> CreateEmitter(
        StructEventEmitterOptions<TEvent>? options = null);

    public SequencedStructEventEmitter<TEvent> CreateSequencedEmitter(
        StructEventEmitterOptions<TEvent>? options = null)
        where TEvent : struct, IStructEvent, ISequencedStructEvent<TEvent>;

    public StructEventInbox<TEvent> CreateInbox(
        StructEventInboxOptions? options = null);

    public StructEventSubscription<TEvent> Subscribe(
        StructEventSubscriptionOptions? options = null);

    public StructEventRouteStats GetStats();
}
```

The route owns:

- route configuration
- subscriber list
- route sequence counter
- route stats

The route does not own one shared queue. It fans out to caller-owned consumers. Each inbox or subscription owns its own bounded backlog and overflow behavior.

### 6.3 Emitter

```csharp
namespace HPD.Events.Struct;

public readonly struct StructEventEmitter<TEvent>
    where TEvent : struct, IStructEvent
{
    public StructEventEmitResult Emit(in TEvent evt);

    public StructEventEmitBatchResult EmitBatch(ReadOnlySpan<TEvent> events);
}
```

Sequenced emitter:

```csharp
namespace HPD.Events.Struct;

public readonly struct SequencedStructEventEmitter<TEvent>
    where TEvent : struct, IStructEvent, ISequencedStructEvent<TEvent>
{
    public StructEventEmitResult Emit(in TEvent evt);

    public StructEventEmitBatchResult EmitBatch(ReadOnlySpan<TEvent> events);
}
```

The sequenced emitter assigns route-local sequence numbers without boxing.

Emitter options are deliberately small and synchronous:

```csharp
namespace HPD.Events.Struct;

public sealed record StructEventEmitterOptions<TEvent>
    where TEvent : struct, IStructEvent
{
    public Func<TEvent, bool>? Filter { get; init; }
}
```

The filter is cold-contract optional and runs synchronously before fan-out. It
must not allocate or block if used in a realtime route.

### 6.4 Inbox

```csharp
namespace HPD.Events.Struct;

public sealed class StructEventInbox<TEvent> : IDisposable
    where TEvent : struct, IStructEvent
{
    public bool TryRead(out TEvent evt);

    public int TryReadBatch(Span<TEvent> destination);
}
```

### 6.5 Subscription

```csharp
namespace HPD.Events.Struct;

public sealed class StructEventSubscription<TEvent> : IDisposable
    where TEvent : struct, IStructEvent
{
    public bool TryRead(out TEvent evt);

    public int TryReadBatch(Span<TEvent> destination);
}
```

The difference between inbox and subscription can remain:

- inbox: caller-owned deterministic lane
- subscription: direct reader subscription

Both are pull-based.

Inbox and subscription handles are sealed classes, not disposable structs.
They allocate at consumer creation time, which is outside the realtime steady
state. This avoids C# struct-copy disposal footguns. Disposal is idempotent and
removes the underlying route registration once.

### 6.6 No Observer API In Core

Remove this concept from the struct-event core:

```csharp
Observe(Func<TEvent, ValueTask>)
```

Reasons:

- It runs user code from the emit path.
- It can block the emitter.
- It can allocate when incomplete `ValueTask` is converted to `Task`.
- It introduces async semantics into a synchronous hot lane.
- It makes it too easy to misuse struct events as a callback bus.

If observation is needed, consumers can create an inbox and drain it themselves.

## 7. StructEvent Options

### 7.1 Route Options

```csharp
namespace HPD.Events.Struct;

public sealed record StructEventRouteOptions
{
    public StructEventConcurrencyMode ConcurrencyMode { get; init; } =
        StructEventConcurrencyMode.MultiProducerMultiConsumer;

    public StructEventStatsMode StatsMode { get; init; } =
        StructEventStatsMode.Minimal;
}
```

Route options describe behavior shared by every consumer of the route. They do not include queue capacity or overflow policy, because those belong to the individual inbox/subscription buffers.

### 7.2 Inbox Options

```csharp
namespace HPD.Events.Struct;

public sealed record StructEventInboxOptions
{
    public int Capacity { get; init; } = 1024;

    public StructEventOverflowMode OverflowMode { get; init; } =
        StructEventOverflowMode.Backpressure;
}
```

### 7.3 Subscription Options

```csharp
namespace HPD.Events.Struct;

public sealed record StructEventSubscriptionOptions
{
    public int Capacity { get; init; } = 1024;

    public StructEventOverflowMode OverflowMode { get; init; } =
        StructEventOverflowMode.Backpressure;
}
```

Consumer options describe each reader's private buffer:

- capacity
- overflow behavior
- future consumer labels or diagnostics policy

The semantic rule is:

```text
A route fans out events; each subscriber owns its own bounded backlog.
```

### 7.4 Concurrency Mode

```csharp
namespace HPD.Events.Struct;

public enum StructEventConcurrencyMode
{
    SingleProducerSingleConsumer,
    MultiProducerSingleConsumer,
    MultiProducerMultiConsumer
}
```

Interpretation:

- `SingleProducerSingleConsumer`: valid only for a route constrained to one emitter and one consumer registration; target for a lock-free SPSC route/buffer shape.
- `MultiProducerSingleConsumer`: multiple emitters and one consumer registration; useful for centralized telemetry drains.
- `MultiProducerMultiConsumer`: multiple emitters and multiple consumer registrations; default general fan-out shape.

The default must match the public route semantics: routes fan out to all
registered consumers unless a stricter route mode is explicitly selected. Once
optimized SPSC or MPSC implementations exist, those modes should either enforce
their emitter/consumer limits at creation time or clearly document fallback to
the general MPMC route implementation.

If the first implementation uses the current lock-based ring for all modes, document it honestly:

```text
allocation-free after warmup, not lock-free
```

Then add a true SPSC implementation as the first performance upgrade.

### 7.5 Overflow Mode

```csharp
namespace HPD.Events.Struct;

public enum StructEventOverflowMode
{
    Backpressure,
    DropOldest,
    DropNewest,
    Reject
}
```

Rules:

- `Backpressure`: do not accept the event and report backpressure.
- `DropOldest`: discard the oldest queued event and accept the new event.
- `DropNewest`: discard the new event.
- `Reject`: reject the new event without counting it as a drop.

Overflow is evaluated per consumer buffer. If one subscriber is full and another has capacity, the subscriber with capacity should still receive the event. Emit results report aggregate fan-out outcomes across subscribers.

No overflow mode should park the producer or allocate a waiter.

### 7.6 Stats Mode

```csharp
namespace HPD.Events.Struct;

public enum StructEventStatsMode
{
    None,
    Minimal,
    Full
}
```

Suggested semantics:

- `None`: no per-emit counters except what is needed for correctness.
- `Minimal`: emitted, accepted, dropped, backpressured/rejected totals.
- `Full`: per-route queue depth, max depth, subscriber writes, subscriber drops, filtered counts, and route stats.

Default should be `Minimal`.

`StatsMode.None` is a no-route-counter mode. It should skip emitted,
accepted, dropped, filtered, depth, max-depth, subscriber-write, and
subscriber-drop counter updates while preserving per-emit result correctness.
It does not imply lock-free operation, and sequenced emit still increments the
route-local sequence counter.

## 8. Emit Results

### 8.1 Single Emit Result

```csharp
namespace HPD.Events.Struct;

public enum StructEventEmitStatus
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
namespace HPD.Events.Struct;

public readonly record struct StructEventEmitResult(
    StructEventEmitStatus Status,
    int SubscriberCount,
    int AcceptedCount,
    int DroppedCount)
{
    public bool Accepted => AcceptedCount > 0;
}
```

### 8.2 Batch Emit Result

```csharp
namespace HPD.Events.Struct;

public readonly record struct StructEventEmitBatchResult(
    int EventCount,
    int AcceptedEvents,
    int DroppedEvents,
    int BackpressuredEvents,
    int RejectedEvents,
    int FilteredEvents,
    int TotalSubscriberWrites,
    int TotalSubscriberDrops);
```

These names match the current useful result model while removing the `LocalStruct` prefix.

Aggregate status precedence is deterministic:

```text
Filtered      when the emitter filter skips before fan-out.
Disposed      when the route/hub is disposed before fan-out.
NoSubscribers when there are no subscribers.
Accepted      when at least one subscriber accepts.
Backpressured when none accept and at least one subscriber reports backpressure.
Rejected      when none accept/backpressure and at least one subscriber rejects.
Dropped       when none accept/backpressure/reject and at least one subscriber drops.
```

`AcceptedCount` and `DroppedCount` remain authoritative for mixed fan-out cases.
For example, if one subscriber accepts and another drops, status is `Accepted`
and `DroppedCount` records the drop.

## 9. Realtime Contract

The struct-event layer should publish a precise realtime contract.

For a cached route, cached emitter, and pre-created inbox/subscription, the following operations must allocate 0 managed bytes after warmup:

```text
StructEventEmitter<T>.Emit
StructEventEmitter<T>.EmitBatch
SequencedStructEventEmitter<T>.Emit
SequencedStructEventEmitter<T>.EmitBatch
StructEventInbox<T>.TryRead
StructEventInbox<T>.TryReadBatch
StructEventSubscription<T>.TryRead
StructEventSubscription<T>.TryReadBatch
overflow/drop/backpressure paths
```

The contract does not apply to:

- hub creation
- route creation
- emitter creation
- inbox/subscription creation
- route disposal
- stats collection
- `GetRouteStats()`
- diagnostic logging outside the lane

### 9.1 Required Allocation Tests

Add tests using `GC.GetAllocatedBytesForCurrentThread()` or a benchmark harness:

```text
CachedEmit_NoSubscribers_AllocatesZero
CachedEmit_WithInbox_AllocatesZero
CachedEmit_DropOldest_AllocatesZero
CachedEmit_DropNewest_AllocatesZero
CachedEmit_Backpressure_AllocatesZero
CachedEmit_Reject_AllocatesZero
CachedBatchEmit_AllocatesZero
CachedTryRead_AllocatesZero
CachedTryReadBatch_AllocatesZero
CachedSequencedEmit_AllocatesZero
```

Warmup must be separated from measured execution.

### 9.2 Required Contention Benchmarks

Add benchmarks for:

```text
SPSC emit/read
MPSC emit/read
DropOldest under full buffer
Backpressure under full buffer
Batch emit
Batch read
StatsMode.None
StatsMode.Minimal
StatsMode.Full
```

The benchmark should report:

- throughput
- p50/p95/p99 emit latency where possible
- managed allocations
- contention cost

## 10. Replay

Replay remains semantic event replay:

```csharp
ReplayTimeline<TEvent>
    where TEvent : Event
```

Do not force struct events into class-event replay.

Do not add struct-event replay in this proposal.

If struct-event replay becomes a real requirement later, add a native struct replay model:

```csharp
StructReplayTimeline<TEvent>
    where TEvent : struct, IStructEvent
```

That model should not inherit from or adapt through class `Event`.

## 11. Testing And Clocks

`ManualClock` and `SystemClock` are useful, but they are not part of the realtime struct-event hot lane.

Move them to:

```text
HPD.Events.Testing
```

or keep production `SystemClock` in a small time package if runtime code needs it.

`ManualClock` should be used for deterministic control-plane/replay tests, not for struct-event emit paths.

## 12. What To Delete Or Move

Delete from `EventBus` and `EventCoordinator`:

```text
LocalStructs property
local struct lane construction
local struct lane disposal responsibility
```

Rename and move:

```text
Abstractions/ILocalStructEventBus.cs
Abstractions/LocalStructEmitResult.cs
Abstractions/LocalStructEventStats.cs
Abstractions/LocalStructOptions.cs
Abstractions/LocalStructInbox.cs
Abstractions/LocalStructSubscription.cs
Abstractions/LocalStructEventRouteExtensions.cs
Core/LocalStructEventBus.cs
Core/LocalStructEventRoute.cs
Core/LocalStructEmitter.cs
Core/LocalStructRingBuffer.cs
Core/LocalStructSubscriber.cs
```

Into:

```text
Struct/Abstractions/IStructEventHub.cs
Struct/Abstractions/StructEventEmitResult.cs
Struct/Abstractions/StructEventStats.cs
Struct/Abstractions/StructEventRouteOptions.cs
Struct/Abstractions/StructEventInboxOptions.cs
Struct/Abstractions/StructEventSubscriptionOptions.cs
Struct/Abstractions/StructEventInbox.cs
Struct/Abstractions/StructEventSubscription.cs
Struct/Core/StructEventHub.cs
Struct/Core/StructEventRoute.cs
Struct/Core/StructEventEmitter.cs
Struct/Core/StructEventRingBuffer.cs
Struct/Core/StructEventSubscriber.cs
```

Remove from struct-event core:

```text
Observe(Func<TEvent, ValueTask>)
LocalStructObserver<TEvent>
LocalStructObserverHandle<TEvent>
```

## 13. Dependency Policy

Official policy after this proposal:

```text
Core media movement contracts should not depend on HPD Events.
Telemetry-enabled low-level implementations may optionally depend on HPD.Events.Struct.
Diagnostics/integration packages may depend on HPD.Events and HPD.Events.Struct.
Realtime packages must not depend on EventBus, EventCoordinator, ReplayTimeline, or Testing for packet/audio hot paths.
```

Recommended dependency map:

| Package | HPD Events dependency |
|---|---|
| `HPD.Audio.Primitives` | none |
| `HPD.Net.Abstractions` | none |
| `HPD.Net.Rtp` | none in core contracts; optional `HPD.Events.Struct` in telemetry-enabled implementation or `HPD.Net.Rtp.Diagnostics` |
| `HPD.Net.Rtcp` | none in core contracts; optional `HPD.Events.Struct` in telemetry-enabled implementation or `HPD.Net.Rtcp.Diagnostics` |
| `HPD.Net.Srtp` | none in core contracts; optional `HPD.Events.Struct` in telemetry-enabled implementation or `HPD.Net.Srtp.Diagnostics` |
| `HPD.Audio.Codecs` | none in abstractions; concrete codec diagnostics may optionally use `HPD.Events.Struct` |
| `HPD.Net.WebRTC` core | preferably none |
| `HPD.Net.WebRTC.Diagnostics` | `HPD.Events`, `HPD.Events.Struct` |
| `HPD.Audio.WebRTC` integration | `HPD.Events`, `HPD.Events.Struct` |
| test/replay tools | `HPD.Events.Replay`, `HPD.Events.Testing` |

This lets low-level realtime packages share struct-event telemetry infrastructure without dragging in the semantic event bus.

## 14. What Becomes Possible

After this split, HPD can do things that are awkward or unsafe today.

### 14.1 Safe Low-Level Dependency

Low-level packages such as RTP, RTCP, SRTP, or realtime pumps can depend on `HPD.Events.Struct` without inheriting:

- class events
- channels
- async enumerables
- replay
- object metadata
- request/response
- event-flow interruption

### 14.2 Shared Realtime Telemetry

Packages can emit high-frequency struct telemetry with one common shape:

```text
RtpPacketLossSample
RtcpJitterSample
AudioPumpCycle
CodecFrameTiming
DatagramQueueDepthSample
SrtpReplayRejectSample
```

No bespoke queue/callback/counter model per package.

### 14.3 Zero-Allocation Diagnostics

Diagnostics can be compatible with realtime loops:

```text
0 B emit after warmup
0 B batch drain after warmup
explicit drop/backpressure result
bounded memory
```

### 14.4 Cleaner Tests

Tests can subscribe to struct-event lanes and assert on behavior without adding test-only hooks:

```csharp
using var inbox = structs.Route<RtpLossSample>().CreateInbox();

pump.Pump(scratch, 1000);

var count = inbox.TryReadBatch(buffer);
```

### 14.5 Clear Fact/Sample Separation

The architecture makes the difference obvious:

```text
Event:
    WebRtcConnectionOpened
    IceConnectionFailed
    AudioConnectionClosed

StructEvent:
    JitterSample
    QueueDepthSample
    PumpCycle
    PacketDropSample
```

## 15. Migration Plan

Because breaking changes are preferred, do not build compatibility aliases.

### Phase 1: Introduce New Namespaces And Names

Create:

```text
HPD.Events.Struct
StructEventHub
StructEventRoute<T>
StructEventEmitter<T>
StructEventInbox<T>
StructEventSubscription<T>
StructEventRouteOptions
StructEventInboxOptions
StructEventSubscriptionOptions
StructEventStats
```

Keep `IStructEvent` and `ISequencedStructEvent<TSelf>`, moved under the struct namespace if packages/namespaces are split.

### Phase 2: Remove Struct Lane From EventBus

Remove:

```text
IEventCoordinator.LocalStructs
EventCoordinator.LocalStructs
EventBus.LocalStructs
```

Update tests and docs to construct `StructEventHub` directly.

### Phase 3: Remove Observer API

Delete:

```text
Observe(Func<TEvent, ValueTask>)
LocalStructObserver<TEvent>
LocalStructObserverHandle<TEvent>
```

Caller-owned inboxes and subscriptions are the only core consumption model.

### Phase 4: Add Route And Consumer Options

Add:

```text
StructEventRouteOptions
StructEventInboxOptions
StructEventSubscriptionOptions
StructEventConcurrencyMode
StructEventOverflowMode
StructEventStatsMode
```

Route options should be fixed at route creation time. Repeated route requests
with equivalent options return the existing route; conflicting options throw.
Inbox and subscription options are fixed when the consumer is created, because
capacity and overflow belong to each consumer buffer.

### Phase 5: Add Allocation Tests

Add the required zero-allocation tests before claiming realtime readiness.

### Phase 6: Add SPSC Ring

Keep the current lock-based ring if needed for the first pass, but add a true SPSC implementation as the first targeted performance mode.

## 16. Acceptance Criteria

The proposal is implemented when:

1. `EventBus` no longer exposes or owns struct-event lanes.
2. `StructEventHub` is independently constructible and disposable.
3. `StructEventHub` has no dependency on `EventBus`, `EventCoordinator`, class `Event`, `Channel<T>`, replay, or event-flow APIs.
4. Public struct-event names use `StructEvent*`, not `LocalStruct*`.
5. `IStructEvent` and `ISequencedStructEvent<TSelf>` remain the value-event contracts.
6. Struct-event consumption is pull-based through inboxes/subscriptions.
7. The struct-event core has no async observer API.
8. Route options include concurrency and stats modes.
9. Inbox and subscription options include capacity and overflow mode.
10. Repeated route requests with conflicting route options throw instead of silently reusing the existing route.
11. Inbox and subscription handles have idempotent disposal and no struct-copy disposal footgun.
12. Aggregate emit status precedence is documented and tested for mixed fan-out outcomes.
13. Timestamp ownership is documented as producer-supplied; default sequenced emitters assign sequence only.
14. Cached emit/read/batch paths have zero managed allocation tests.
15. Overflow paths have zero managed allocation tests.
16. Sequenced emit has zero managed allocation tests.
17. Class-event replay remains `ReplayTimeline<TEvent> where TEvent : Event`.
18. There is no class-event to struct-event bridge in HPD Events.
19. There is no struct-event to class-event bridge in HPD Events.
20. Docs clearly state that `Event` is for semantic facts and `StructEvent` is for process-local realtime value events.

## 17. Final Decision

Break the current blended model.

Keep `Event` for semantic control-plane and workflow facts.

Keep `StructEvent` naming for process-local realtime value lanes.

Rename the current local struct API to the cleaner `StructEvent*` family, remove it from `EventBus`, remove async observers from the struct-event core, add route and consumer options, add allocation tests, and keep replay semantic.

The official architecture becomes:

```text
EventBus
    semantic class events

StructEventHub
    realtime struct-event lanes

ReplayTimeline
    semantic event replay

Testing
    deterministic clocks and helpers
```

There is no bridge. There is no retrofit. Components choose the right event family at the point of emission.
