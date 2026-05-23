# Engineering Proposal: HPD Events Execution Architecture

**Document:** `HPD.Events.BusInboxArchitecture.Proposal.md`  
**Version:** 2.0.0  
**Date:** May 2026  
**Status:** Proposal  
**Authors:** System Architect, Engineering Team  
**Target:** `HPD.Events`

---

## Executive Summary

HPD Events already contains the right primitives:

1. Event publishing with fan-out to matching subscribers
2. Callback subscriptions processed by background pumps
3. Direct `ChannelReader<T>` subscriptions for caller-owned processing loops
4. Struct event routing for process-local hot paths
5. Request/response waiters for bidirectional workflows
6. Interruptible stream tracking for cancelable grouped event flows
7. Parent event bubbling for hierarchical runtimes

The architectural issue is not that the router is wrong. The issue is that callers can easily choose the wrong execution model.

There are two different consumption models:

```csharp
events.Subscribe<FinanceEvent>(HandleAsync);
```

This registers an observer callback. The event system owns the pump. `Emit()` only means the event was accepted into the subscriber mailbox; it does not mean the callback has completed.

```csharp
await using var inbox = events.CreateInbox<FinanceEvent>();
await foreach (var evt in inbox.Reader.ReadAllAsync(ct))
{
    ProcessEvent(evt);
}
```

This creates an owned event lane. The caller owns ordering, processing, cancellation, and completion behavior.

The main fix is therefore behavioral:

- Observers use callback subscriptions.
- Deterministic engines use owned inboxes.
- Emit-only components depend only on an emit surface.
- Request/response users depend on a request/response surface.
- Struct hot-path users depend on a struct event surface.
- Parent bubbling remains explicit instead of disappearing during the split.

Names may change to make this clearer, but the rename is not the architecture. The architecture is the separation of execution responsibilities.

---

## 1. Goals

### 1.1 Make Execution Ownership Obvious

HPD Events should make it difficult to confuse these two operations:

- "Run this callback when matching events arrive."
- "Give me a reader so I can own the processing loop."

Both are useful. They should not be treated as interchangeable.

### 1.2 Preserve the Existing Router Model

The current internal implementation is close to what we want:

- bounded per-subscriber mailboxes
- backpressure behavior through `BoundedChannelFullMode`
- type matching with optional derived-type inclusion
- background handler pumps for callback subscribers
- direct readers for deterministic consumers
- stream interruption diagnostics
- parent bubbling
- request/response waiters

This proposal does not require replacing `EventChannelRouter`. It requires putting clearer contracts around it.

### 1.3 Support Deterministic Consumers

Systems like Rhodium, workflow runners, replay engines, ingestion loops, and simulations should process domain events from owned readers.

They should not process their primary domain event loop through `SubscribeAny()` or callback handlers.

### 1.4 Keep Observer Subscriptions Cheap and Isolated

Logging, UI updates, telemetry, audit, tracing, and monitoring should remain callback subscriptions. These consumers should not stall producers by default.

### 1.5 Use Narrow Dependencies

Most components do not need the full event system. Dependencies should describe intent:

- emit-only code depends on an event publisher
- observer code depends on observer subscription APIs
- deterministic code depends on inbox creation
- request/response code depends on request/response APIs
- struct hot-path code depends on struct event APIs
- hierarchy setup code depends on parent-linking APIs

---

## 2. Problem Statement

### 2.1 Callback Subscriptions Are Not Completion Semantics

Callback subscriptions are processed by background pumps.

```csharp
events.Subscribe<SomeEvent>(async evt =>
{
    await HandleAsync(evt);
});

events.Emit(new SomeEvent());
```

After `Emit()` returns, `HandleAsync()` may not have run yet. That is correct behavior for observers, but it is wrong for code that thinks it owns the event loop.

This is the central bug class.

### 2.2 Locks Do Not Create Event-Lane Ownership

A callback can serialize its own body:

```csharp
using var subscription = events.SubscribeAny(evt =>
{
    lock (gate)
    {
        ProcessEvent(evt);
    }

    return ValueTask.CompletedTask;
});
```

The lock prevents concurrent callback execution inside that handler. It does not change who owns scheduling, queueing, cancellation, or completion. The event bus still owns the pump.

For deterministic engines, this is the wrong contract.

### 2.3 Direct Readers Need Deterministic Defaults

A caller-owned inbox is often used because every event matters. Dropping from that lane should be an explicit choice.

Observer callback subscriptions can default to lossy behavior because slow observers should not stall the producer unless explicitly configured.

Therefore:

- callback subscriptions default to `DropOldest`
- owned inboxes default to `Wait`

### 2.4 The Current Interface Encourages Over-Capable Dependencies

A monolithic event coordinator surface lets emit-only components receive APIs for subscribing, request/response, struct events, streams, hierarchy, and stats.

That makes tests larger, fakes less honest, and ownership harder to see.

---

## 3. Execution Model

### 3.1 Event Publishing

Publishing assigns a sequence number and fans the event out to matching local mailboxes. If a parent bus is configured, the event bubbles to the parent.

```csharp
publisher.Emit(evt);
await publisher.EmitAsync(evt, ct);
```

`Emit()` is not a handler-completion API.

`EmitAsync()` waits only when a matching mailbox requests backpressure. It still does not wait for callback handlers to complete their work.

### 3.2 Observer Subscriptions

Observer subscriptions register callback handlers.

```csharp
IDisposable subscription = observers.Subscribe<TextDeltaEvent>(evt =>
{
    Render(evt);
    return ValueTask.CompletedTask;
});
```

The event system creates the mailbox and owns the background pump.

Use this for:

- logs
- telemetry
- trace capture
- audit
- UI notifications
- monitoring
- secondary side effects

Do not use this for primary deterministic engine loops.

### 3.3 Owned Inboxes

An inbox is a caller-owned mailbox with a `ChannelReader<T>`.

```csharp
await using var inbox = inboxes.CreateInbox<FinanceEvent>(
    EventInboxOptions.Deterministic());

await foreach (var evt in inbox.Reader.ReadAllAsync(ct))
{
    ProcessEvent(evt);
}
```

The bus still routes events into the mailbox. The caller owns consumption.

Use this for:

- trading engines
- workflow runners
- replay engines
- deterministic tests
- ingestion processors
- simulations
- CLIs or native bridges that need ordered event rendering

### 3.4 Channel Inboxes

Some consumers need all events on one channel:

```csharp
await using var controlInbox = inboxes.CreateChannelInbox(EventChannel.Control);
```

This is still a caller-owned reader. It is not a callback subscription.

### 3.5 Struct Events

Struct events are process-local hot-path events. They are not semantic workflow events and do not bubble through parent class-event buses.

Struct events need the same distinction:

- callback observer: `Subscribe<TStruct>(handler)`
- owned reader: `CreateInbox<TStruct>()`
- hot-path pre-bound emitter: `CreateEmitter<TStruct>()`

### 3.6 Request/Response

Request/response is a separate behavior layered on top of event routing.

The response waiter must be registered before the request is emitted. `Respond()` and `TryRespond()` complete a matching pending waiter by request id.

Components that need this behavior should depend on a request/response contract explicitly.

### 3.7 Parent Bubbling

Parent bubbling remains supported, but it should be exposed through an explicit hierarchy contract instead of being lost in a broad coordinator interface.

Hierarchy setup is infrastructure behavior. Most publishers and observers do not need access to it.

---

## 4. Proposed Public Contracts

The exact concrete type names can be finalized during implementation. The important part is the contract split.

### 4.1 Emit-Only Contract

```csharp
public interface IEventPublisher
{
    void Emit(Event evt);

    ValueTask EmitAsync(Event evt, CancellationToken ct = default);
}
```

Connectors, middleware contexts, and most producers should depend on this when they only emit events.

### 4.2 Observer Contract

```csharp
public interface IEventObserverBus
{
    IDisposable Subscribe<TEvent>(
        Func<TEvent, ValueTask> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : Event;

    IDisposable SubscribeAny(
        Func<Event, ValueTask> handler,
        EventSubscriptionOptions? options = null);
}
```

This contract is for components that attach background observers.

### 4.3 Inbox Contract

```csharp
public interface IEventInboxSource
{
    EventInbox<TEvent> CreateInbox<TEvent>(
        EventInboxOptions? options = null)
        where TEvent : Event;

    EventInbox<Event> CreateChannelInbox(
        EventChannel channel,
        EventInboxOptions? options = null);
}
```

This contract is for deterministic consumers that own their event loop.

### 4.4 Class Event Bus Composition

```csharp
public interface IEventBus :
    IEventPublisher,
    IEventObserverBus,
    IEventInboxSource
{
    IEventFlowRegistry EventFlows { get; }

    EventBusStats GetStats();
}
```

`IEventBus` is the convenient composed surface for places that truly need the full class-event bus.

### 4.5 Request/Response Contract

```csharp
public interface IRequestResponseBus
{
    Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TRequest : Event, IBidirectionalEvent
        where TResponse : Event;

    void Respond(string requestId, Event response);

    bool TryRespond(string requestId, Event response);
}
```

Request/response remains available, but consumers should ask for it deliberately.

### 4.6 Hierarchy Contract

```csharp
public interface IHierarchicalEventBus
{
    void SetParent(IEventBus parent);
}
```

This keeps parent bubbling explicit. If implementation needs a concrete parent type for child registration and cycle detection, that can remain internal, but the public capability should not be hidden.

### 4.7 Struct Event Contract

```csharp
public interface IStructEventBus
{
    bool TryEmit<TEvent>(in TEvent evt)
        where TEvent : struct, IStructEvent;

    ValueTask EmitAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : struct, IStructEvent;

    StructInbox<TEvent> CreateInbox<TEvent>(
        StructInboxOptions? options = null)
        where TEvent : struct, IStructEvent;

    IDisposable Subscribe<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : struct, IStructEvent;

    StructEmitter<TEvent> CreateEmitter<TEvent>(
        StructEmitterOptions<TEvent>? options = null)
        where TEvent : struct, IStructEvent;
}
```

Struct events remain separate from class events.

---

## 5. Data Types

### 5.1 Event Inbox

```csharp
public readonly struct EventInbox<TEvent> : IAsyncDisposable
    where TEvent : Event
{
    private readonly ChannelWriter<TEvent>? _writer;
    private readonly Action<ChannelWriter<TEvent>>? _dispose;

    internal EventInbox(
        ChannelReader<TEvent> reader,
        ChannelWriter<TEvent> writer,
        Action<ChannelWriter<TEvent>> dispose)
    {
        Reader = reader;
        _writer = writer;
        _dispose = dispose;
    }

    public ChannelReader<TEvent> Reader { get; }

    public ValueTask DisposeAsync()
    {
        if (_writer is not null && _dispose is not null)
            _dispose(_writer);

        return ValueTask.CompletedTask;
    }
}
```

### 5.2 Event Inbox Options

```csharp
public sealed record EventInboxOptions
{
    public int Capacity { get; init; } = 1024;

    public BoundedChannelFullMode FullMode { get; init; } =
        BoundedChannelFullMode.Wait;

    public bool IncludeDerivedTypes { get; init; } = true;

    public EventChannel? Channel { get; init; }

    public static EventInboxOptions Deterministic(int capacity = 4096) =>
        new()
        {
            Capacity = capacity,
            FullMode = BoundedChannelFullMode.Wait,
            IncludeDerivedTypes = true
        };

    public static EventInboxOptions LatestOnly(int capacity = 1) =>
        new()
        {
            Capacity = capacity,
            FullMode = BoundedChannelFullMode.DropOldest,
            IncludeDerivedTypes = true
        };

    public static EventInboxOptions LossyTelemetry(int capacity = 1024) =>
        new()
        {
            Capacity = capacity,
            FullMode = BoundedChannelFullMode.DropWrite,
            IncludeDerivedTypes = true
        };

    internal EventSubscriptionOptions ToSubscriptionOptions() =>
        new()
        {
            Capacity = Capacity,
            FullMode = FullMode,
            IncludeDerivedTypes = IncludeDerivedTypes,
            Channel = Channel
        };
}
```

`ToSubscriptionOptions()` is intentionally called out. Internally, inboxes can reuse the existing subscriber machinery while still exposing inbox-specific defaults.

### 5.3 Observer Subscription Options

```csharp
public sealed record EventSubscriptionOptions
{
    public int Capacity { get; init; } = 1024;

    public BoundedChannelFullMode FullMode { get; init; } =
        BoundedChannelFullMode.DropOldest;

    public bool IncludeDerivedTypes { get; init; } = true;

    public EventChannel? Channel { get; init; }
}
```

Observer subscriptions remain lossy by default. A slow observer should not stall the producer unless the caller explicitly chooses `Wait`.

### 5.4 Event Bus Stats

```csharp
public readonly record struct EventBusStats(
    int SubscriberCount,
    int InboxCount,
    int TotalQueued,
    int TotalDropped,
    int MaxSubscriberDepth);
```

`InboxCount` counts direct reader subscribers. `SubscriberCount` counts all class-event subscribers unless a more specific breakdown is added later.

### 5.5 Struct Inbox

```csharp
public readonly struct StructInbox<TEvent> : IAsyncDisposable
    where TEvent : struct, IStructEvent
{
    private readonly ChannelWriter<TEvent>? _writer;
    private readonly Action<ChannelWriter<TEvent>>? _dispose;

    internal StructInbox(
        ChannelReader<TEvent> reader,
        ChannelWriter<TEvent> writer,
        Action<ChannelWriter<TEvent>> dispose)
    {
        Reader = reader;
        _writer = writer;
        _dispose = dispose;
    }

    public ChannelReader<TEvent> Reader { get; }

    public ValueTask DisposeAsync()
    {
        if (_writer is not null && _dispose is not null)
            _dispose(_writer);

        return ValueTask.CompletedTask;
    }
}
```

### 5.6 Struct Inbox Options

```csharp
public sealed record StructInboxOptions
{
    public int Capacity { get; init; } = 1024;

    public BoundedChannelFullMode FullMode { get; init; } =
        BoundedChannelFullMode.Wait;
}
```

Struct inboxes default to deterministic backpressure. Lossy struct handling remains available by setting `FullMode`.

---

## 6. Concrete Implementation

### 6.1 Concrete Event Bus

The concrete implementation can be one class that composes all event capabilities:

```csharp
public sealed class EventBus :
    IEventBus,
    IRequestResponseBus,
    IHierarchicalEventBus,
    IStructEventBus,
    IDisposable
{
    private readonly EventChannelRouter _events;
    private readonly StructEventRouter _structs = new();

    public EventBus(
        Func<Event, Event>? eventEnricher = null,
        Func<Event, bool>? eventFilter = null)
    {
        _events = new EventChannelRouter(eventEnricher, eventFilter);
    }

    public void Emit(Event evt) => _events.Emit(evt);

    public ValueTask EmitAsync(Event evt, CancellationToken ct = default) =>
        _events.EmitAsync(evt, ct);

    public IDisposable Subscribe<TEvent>(
        Func<TEvent, ValueTask> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : Event =>
        _events.Subscribe(handler, options);

    public EventInbox<TEvent> CreateInbox<TEvent>(
        EventInboxOptions? options = null)
        where TEvent : Event =>
        _events.CreateInbox<TEvent>(options);
}
```

This keeps implementation simple while allowing consumers to depend on narrower interfaces.

### 6.2 Event Router Inbox Methods

`EventChannelRouter` should create inboxes with inbox options, then convert internally to the existing subscriber options:

```csharp
public EventInbox<TEvent> CreateInbox<TEvent>(
    EventInboxOptions? options = null)
    where TEvent : Event
{
    ObjectDisposedException.ThrowIf(_disposed, this);

    options ??= new EventInboxOptions();

    var subscriber = CreateSubscriber<TEvent>(
        isStream: true,
        options.ToSubscriptionOptions());

    return new EventInbox<TEvent>(
        subscriber.Reader,
        subscriber.Writer,
        writer => RemoveSubscriberByWriter(writer));
}

public EventInbox<Event> CreateChannelInbox(
    EventChannel channel,
    EventInboxOptions? options = null)
{
    options = (options ?? new EventInboxOptions()) with { Channel = channel };
    return CreateInbox<Event>(options);
}
```

The internal name `isStream` can be renamed to `isInbox` during cleanup, but the behavior does not need to change.

### 6.3 Parent Bubbling

Parent bubbling should continue to use cycle detection and child registration.

The public API should expose only the setup capability:

```csharp
public void SetParent(IEventBus parent);
```

The implementation may require the parent to be the concrete `EventBus` for child registration. If a non-`EventBus` implementation is passed, it can still receive bubbled events through `IEventPublisher`, but it cannot participate in internal response-waiter hierarchy scanning. That limitation should be documented or prevented with a more specific parent type.

Recommended implementation rule:

- `SetParent(EventBus parent)` on the concrete type for full hierarchy support
- `IHierarchicalEventBus.SetParent(IEventBus parent)` only if partial parent support is acceptable

For the first implementation, prefer full correctness over abstraction purity.

### 6.4 Request/Response Hierarchy

`TryRespond()` currently searches the router and child routers for pending response waiters. Preserve this behavior.

If the public hierarchy contract becomes narrower, ensure request/response still works across parent/child buses when both are concrete event buses.

### 6.5 Struct Event Bus

Struct routing can remain exact-type and local.

Rename or alias the public reader-returning path to `CreateInbox<TStruct>()`, but keep the important behavior:

- no class-event parent bubbling
- exact struct type matching
- optional sequence assignment through `StructEmitter<T>`
- bounded per-subscriber channels
- deterministic default backpressure for owned readers

---

## 7. Naming Guidance

This proposal is not primarily about renaming. The required improvement is choosing the right execution model.

That said, names should help prevent future misuse.

Recommended names:

| Behavior | Preferred Name |
|----------|----------------|
| Publish event | `Emit`, `EmitAsync` |
| Register callback observer | `Subscribe`, `SubscribeAny` |
| Create caller-owned reader | `CreateInbox` |
| Create caller-owned channel reader | `CreateChannelInbox` |
| Emit struct event | `TryEmit`, `EmitAsync` on `IStructEventBus` |
| Create struct reader | `CreateInbox` on `IStructEventBus` |
| Register struct callback observer | `Subscribe` on `IStructEventBus` |

Keeping older names is technically possible if the behavior is fixed. For example, `SubscribeStream<T>()` can still return a caller-owned reader. But it remains easy to confuse with callback subscriptions.

Because there are no external consumers today, the preferred implementation should use the clearer names and avoid compatibility shims. If the team decides to keep existing names temporarily, the documentation and tests must still enforce the behavioral rule:

**deterministic consumers own readers; observers use callbacks.**

---

## 8. Rhodium Integration

### 8.1 Current Issue

Rhodium currently processes finance events through a background observer pattern:

```csharp
using var subscription = events.SubscribeAny(evt =>
{
    if (evt is FinanceEvent financeEvt)
    {
        lock (gate)
        {
            ProcessEvent(financeEvt);
        }
    }

    return ValueTask.CompletedTask;
});
```

This serializes the callback body but does not make Rhodium the owner of the event lane.

### 8.2 Required Behavior

Rhodium must drain a deterministic `FinanceEvent` inbox directly:

```csharp
public async Task RunAsync(CancellationToken ct = default)
{
    foreach (var (strategy, _) in _tree.Nodes.OrderBy(static n => n.Node.Depth))
        strategy.Initialize(_runtime);

    BuildDispatchContexts();

    if (UseParallelDispatch)
    {
        _parallelDispatchState?.Dispose();
        _parallelDispatchState = new ParallelDispatchState(_tree)
        {
            ParallelThreshold = ParallelThreshold
        };
    }

    await using var inbox = _events.CreateInbox<FinanceEvent>(
        EventInboxOptions.Deterministic());

    var connectorTask = _connector.StartAsync(BuildSubscriptions(), _events, ct);

    await foreach (var evt in inbox.Reader.ReadAllAsync(ct))
    {
        ProcessEvent(evt);

        if (connectorTask.IsCompleted && !await inbox.Reader.WaitToReadAsync(ct))
            break;
    }

    await connectorTask;
}
```

The final loop should be refined during implementation to handle connector completion, channel completion, and cancellation without hanging.

The invariant is fixed:

**Rhodium production trading flow must not process `FinanceEvent` through `SubscribeAny()`.**

### 8.3 Connector Dependency

Connectors emit events. They should not receive subscription, inbox, struct, request/response, or hierarchy APIs unless they use them.

Preferred connector contract:

```csharp
public interface IConnector
{
    Task StartAsync(
        IEnumerable<Subscription> subscriptions,
        IEventPublisher events,
        CancellationToken ct);
}
```

If a connector truly needs stats, streams, or request/response later, that dependency can be widened deliberately.

---

## 9. HPD-Agent And Framework Integration

### 9.1 Agent Observers

Agent convenience APIs can remain ergonomic:

```csharp
agent.Subscribe<TextDeltaEvent>(...);
agent.SubscribeAny(...);
```

Internally these delegate to observer subscription APIs.

### 9.2 Deterministic Agent Consumers

CLI streaming, native callbacks, test harnesses, and renderers that need ordered event ownership should use inboxes:

```csharp
await using var inbox = agent.CreateInbox<AgentEvent>();

await foreach (var evt in inbox.Reader.ReadAllAsync(ct))
{
    Render(evt);
}
```

### 9.3 Middleware Contexts

Middleware contexts should usually depend on `IEventPublisher`:

```csharp
context.Emit(new TextDeltaEvent(...));
```

Where middleware needs request/response, provide `IRequestResponseBus` explicitly or expose it through a composed runtime context.

---

## 10. Migration Plan

Because there are no external consumers, compatibility shims are not required.

### Phase 1: Add New Contracts And Types

Create:

```text
Abstractions/IEventPublisher.cs
Abstractions/IEventObserverBus.cs
Abstractions/IEventInboxSource.cs
Abstractions/IEventBus.cs
Abstractions/IRequestResponseBus.cs
Abstractions/IHierarchicalEventBus.cs
Abstractions/IStructEventBus.cs
Abstractions/EventInbox.cs
Abstractions/EventInboxOptions.cs
Abstractions/EventBusStats.cs
Abstractions/StructInbox.cs
Abstractions/StructInboxOptions.cs
```

Gate:

- HPD-Events builds
- new focused API tests compile

### Phase 2: Implement Inboxes

Update `EventChannelRouter`:

- add `CreateInbox<T>()`
- add `CreateChannelInbox()`
- preserve existing subscriber fan-out behavior
- make inbox defaults deterministic through `EventInboxOptions`

Gate:

- inbox tests pass
- disposing an inbox removes its subscriber
- `EmitAsync()` waits when inbox `FullMode` is `Wait`

### Phase 3: Split Struct Surface

Update `StructEventRouter`:

- add `CreateInbox<TStruct>()`
- keep handler subscriptions as `Subscribe<TStruct>(handler)`
- keep hot-path emitter as `CreateEmitter<TStruct>()`
- keep struct events exact-type and process-local

Gate:

- struct tests pass
- struct events do not bubble to parent buses
- class event subscriptions do not receive struct events

### Phase 4: Preserve Hierarchy And Request/Response

Update parent setup and request/response tests:

- parent bubbling still works
- cycle detection still works
- `TryRespond()` still finds pending waiters in the bus hierarchy
- duplicate request ids still fail
- response type mismatches still fail
- waiter registration still happens before request emission

Gate:

- hierarchy tests pass
- request/response tests pass

### Phase 5: Rhodium Migration

Update:

- `TradingHost`
- `IConnector`
- `ReplayConnector`
- connectivity tests

Gate:

- Rhodium drains `FinanceEvent` from an inbox
- production trading flow does not use `SubscribeAny()`
- connectors depend on `IEventPublisher`
- connectivity tests pass

### Phase 6: Framework Migration

Update HPD-Agent, HPD-Graph, HPD-Auth, and tests by dependency intent:

- emit-only code uses `IEventPublisher`
- observers use `IEventObserverBus` or `IEventBus`
- deterministic consumers use `IEventInboxSource`
- request/response users use `IRequestResponseBus`
- struct hot-path users use `IStructEventBus`
- hierarchy setup uses the concrete bus or `IHierarchicalEventBus`

Gate:

- affected framework tests pass
- broad fake coordinators are replaced by small fakes for the exact capability under test

### Phase 7: Remove Stale APIs

Delete old APIs after all call sites migrate.

Candidates:

```text
IEventCoordinator
EventCoordinator
EventStreamSubscription
StructSubscription
EventCoordinatorStats
SubscribeStream
SubscribeChannel
TryEmitStruct
EmitStructAsync
CreateStructEmitter
```

Some names can be retained if the team chooses, but there should be no duplicate compatibility surface that lets new code use both models ambiguously.

---

## 11. Test Plan

### 11.1 Event Publishing

- `Emit()` assigns sequence numbers
- `Emit()` fans out to all matching subscribers
- `EmitAsync()` waits for mailbox capacity only when `FullMode.Wait` is configured
- event filtering and enrichment still apply before routing
- stream-interrupted events are dropped and emit diagnostics

### 11.2 Observer Subscriptions

- `Subscribe<T>()` receives matching typed events
- derived-type inclusion follows options
- channel filtering follows options
- `SubscribeAny()` receives all class events
- handler pumps run independently
- handler faults remove only the failing subscriber
- handler faults emit `EventSubscriberFaultedEvent`

### 11.3 Inboxes

- `CreateInbox<T>()` returns a reader and does not start a handler pump
- inboxes default to `FullMode.Wait`
- inboxes can opt into lossy behavior
- `CreateChannelInbox()` filters by channel
- disposing an inbox removes its subscriber
- inbox depth contributes to stats

### 11.4 Streams

- active streams count correctly
- interrupted streams cause interruptible events to drop
- non-interruptible events still deliver
- dropped stream events produce `EventDroppedEvent`
- stream emitted and dropped counters update

### 11.5 Hierarchy

- child events bubble to parent
- parent assignment rejects self-parenting
- parent assignment rejects cycles
- disposed children unregister from parents

### 11.6 Request/Response

- waiter is registered before request emission
- matching response completes the request
- timeout throws `TimeoutException`
- cancellation propagates
- duplicate request ids fail
- response type mismatch fails
- response lookup works across supported parent/child hierarchy

### 11.7 Struct Events

- struct events route by exact type
- struct inboxes receive matching events
- struct handler subscriptions run on pumps
- struct emitters apply filters
- struct emitters assign sequence numbers when configured
- struct events do not enter class-event parent bubbling

### 11.8 Static Guards

Static guards should ban stale APIs without banning valid inbox consumption.

Avoid a broad string ban on `ReadAllAsync`. In the new architecture, this is valid:

```csharp
await foreach (var evt in inbox.Reader.ReadAllAsync(ct))
{
    ProcessEvent(evt);
}
```

Instead, guard against stale event-system APIs:

```csharp
Assert.DoesNotContain("IEventCoordinator", source);
Assert.DoesNotContain("EventCoordinator", source);
Assert.DoesNotContain("EventStreamSubscription", source);
Assert.DoesNotContain("StructSubscription", source);
Assert.DoesNotContain("SubscribeStream", source);
Assert.DoesNotContain("SubscribeChannel", source);
```

Also continue banning old queue-era coordinator APIs if they still exist in source history:

```csharp
Assert.DoesNotContain("EmitUpstream", source);
Assert.DoesNotContain(".TryRead(", source);
```

If `ReadAllAsync` must be guarded, make it targeted to banned coordinator/global queue types, not to local `ChannelReader<T>` inbox usage.

---

## 12. Success Criteria

- [ ] Event publishing remains simple through `Emit` and `EmitAsync`
- [ ] `Emit()` and `EmitAsync()` are not documented or treated as handler-completion APIs
- [ ] Observer callbacks remain available through `Subscribe<T>` and `SubscribeAny`
- [ ] Primary deterministic consumers use owned inbox readers
- [ ] Inboxes default to deterministic backpressure
- [ ] Observer subscriptions default to isolation-friendly dropping
- [ ] Emit-only components depend on `IEventPublisher`
- [ ] Connectors depend on `IEventPublisher`
- [ ] Request/response users depend on `IRequestResponseBus`
- [ ] Struct hot-path users depend on `IStructEventBus`
- [ ] Parent bubbling remains supported and explicitly tested
- [ ] Request/response hierarchy behavior remains supported and explicitly tested
- [ ] Rhodium drains `FinanceEvent` through an inbox
- [ ] Rhodium production trading flow does not use `SubscribeAny()` for primary event processing
- [ ] Static guards ban stale event API names without banning valid inbox `ReadAllAsync` loops
- [ ] Broad fake coordinators are replaced by small fakes for the exact capability under test
- [ ] No compatibility shim preserves ambiguous old and new APIs side by side

---

## 13. Design Rationale

The core event system already has a good mechanical shape. The missing piece is making execution ownership explicit.

Callback subscriptions are excellent for observers because they decouple side effects from producers. They are not appropriate when the consuming component must own ordering, processing, and lifecycle.

Owned inboxes are excellent for deterministic engines because they make the processing loop visible in the consumer. The code that needs determinism can read, await, cancel, drain, and finish on its own terms.

The dependency split is equally important. A connector that only emits should not receive APIs for subscribing, request/response, parent hierarchy, struct routing, and stats. Narrow contracts make production code clearer and tests smaller.

Renaming APIs can help reinforce the model, especially replacing stream-subscription language with inbox language. But the durable architectural rule is independent of the name:

> Observers subscribe with callbacks. Deterministic consumers own inbox readers.

---

*Document Version: 2.0.0*  
*Generated: May 2026*
