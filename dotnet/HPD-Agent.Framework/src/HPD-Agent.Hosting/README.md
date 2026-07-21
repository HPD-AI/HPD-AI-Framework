# HPD-Agent.Hosting

Shared hosting abstractions for HPD-Agent Framework. Provides DTOs, lifecycle management, and serialization for building agent hosting platforms.

## Install

```bash
dotnet add package HPD-Agent.Hosting
```

## Use When

Use this package when you need this HPD Agent capability in an agent application.

## Pre-1.0 API Evolution

HPD-Agent hosting APIs are still pre-1.0. Until `1.0.0`, releases may refine
DTOs, route names, service contracts, and persistence projections as the hosting
surface stabilizes. The current hosting model uses sessions containing threads.

## Thread Executions

Hosting coordinates exclusive thread executions, persists their lifecycle, and
exposes historical execution records separately from live `activeExecution`
ownership. See the core
[thread execution lifecycle](../HPD-Agent/Session/THREAD_EXECUTION_LIFECYCLE.md).

## Fork Groups

Hosting DTOs expose fork groups, but hosting does not define fork semantics.
`AgentThreadService` maps the lower-level
`ThreadForkGraph.BuildVisibleForkGroups(...)` projection from `HPD-Agent`.

Use the core session projection when building custom hosts so ASP.NET, TUI,
desktop, and tests all agree on thread/fork navigation.
