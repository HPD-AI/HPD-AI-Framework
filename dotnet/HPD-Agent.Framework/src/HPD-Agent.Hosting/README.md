# HPD-Agent.Hosting

Shared hosting abstractions for HPD-Agent Framework. Provides DTOs, lifecycle management, and serialization for building agent hosting platforms.

## Install

```bash
dotnet add package HPD-Agent.Hosting
```

## Use When

Use this package when you need this HPD Agent capability in an agent application.

## Compatibility

Starting with `1.0.0`, hosted DTOs, documented route names, service contracts,
and persistence projections follow semantic versioning. Breaking changes require
a new major version and a documented migration path. The current hosting model
uses sessions containing threads.

## Fork Groups

Hosting DTOs expose fork groups, but hosting does not define fork semantics.
`AgentThreadService` maps the lower-level
`ThreadForkGraph.BuildVisibleForkGroups(...)` projection from `HPD-Agent`.

Use the core session projection when building custom hosts so ASP.NET, TUI,
desktop, and tests all agree on thread/fork navigation.
