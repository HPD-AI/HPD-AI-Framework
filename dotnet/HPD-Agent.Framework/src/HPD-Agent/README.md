# HPD-Agent.Framework

A middleware-driven agentic AI framework built on Microsoft.Extensions.AI for building intelligent, tool-using agents.

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

## Thread Executions

A thread execution is one accepted input holding a thread's exclusive execution
slot. Its durable start/finish events describe history, while live
`ActiveExecution` state remains authoritative for ownership and interruption.

See [Session/THREAD_EXECUTION_LIFECYCLE.md](Session/THREAD_EXECUTION_LIFECYCLE.md).
