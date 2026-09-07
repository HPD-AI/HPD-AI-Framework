# HPD-Agent.Framework

A middleware-driven agentic AI framework built on Microsoft.Extensions.AI for building intelligent, tool-using agents.

## Thread-scoped event subscriptions

Subscriptions without a thread key observe only events owned by that `Agent` instance. They include
the agent's per-run coordinators, but exclude events originating in independently owned subagents:

```csharp
using var local = agent.Subscribe<TextDeltaEvent>(evt => Console.Write(evt.Text));
```

Use the complete `(SessionId, ThreadId)` key to observe one concrete child invocation:

```csharp
var child = new ThreadKey(childSessionId, childThreadId);
using var exact = agent.Subscribe<TextDeltaEvent>(child, evt => Console.Write(evt.Text));
```

Request hierarchy explicitly to supervise an entire branch at every depth. Sibling branches are
excluded, and each delivery retains its root-to-origin route:

```csharp
using var subtree = agent.SubscribeAny(
    child,
    AgentEventHierarchy.ThreadAndDescendants,
    evt => Observe(evt));
```

Thread-keyed subscriptions exclude threadless events. Live descendant delivery multiplexes events
from independent thread journals; it does not merge their journal cursors. Callbacks run on the
subscription's mailbox pump, publication does not wait for callback completion, and disposal stops
observation without stopping execution or event bubbling. Framework infrastructure that truly needs
every owner must opt into global observation explicitly.

## Install

```bash
dotnet add package HPD-Agent.Framework
```

## Use When

Use this package when you need this HPD Agent capability in an agent application.

## Pre-1.0 API Evolution

HPD-Agent is still pre-1.0. Until `1.0.0`, minor and patch releases may refine
public APIs, persistence shapes, and hosting contracts as the framework settles.
The current conversation model is session-owned threads with event-sourced thread
history.

## Session Fork Graph

Use `ThreadForkGraph.BuildVisibleForkGroups(...)` when rendering thread/fork
navigation. `Thread.ForkedFrom` is direct lineage; fork groups are semantic
choice points derived from the shared conversation boundary.

See [Session/THREAD_FORK_GRAPH.md](Session/THREAD_FORK_GRAPH.md).
