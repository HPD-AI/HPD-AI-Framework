# HPD-Agent.Framework

A middleware-driven agentic AI framework built on Microsoft.Extensions.AI for building intelligent, tool-using agents.

## Install

```bash
dotnet add package HPD-Agent.Framework
```

## Use When

Use this package when you need this HPD Agent capability in an agent application.

## Session Fork Graph

Use `ThreadForkGraph.BuildVisibleForkGroups(...)` when rendering branch/fork
navigation. `Thread.ForkedFrom` is direct lineage; fork groups are semantic
choice points derived from the shared conversation boundary.

See [Session/THREAD_FORK_GRAPH.md](Session/THREAD_FORK_GRAPH.md).
