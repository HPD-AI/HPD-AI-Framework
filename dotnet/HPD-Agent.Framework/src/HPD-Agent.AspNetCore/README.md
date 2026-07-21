# HPD-Agent.AspNetCore

ASP.NET Core hosting layer for HPD-Agent Framework. Provides minimal-code APIs with committed, resumable SSE observation.

## Install

```bash
dotnet add package HPD-Agent.AspNetCore
```

## Use When

Use this package when you need this HPD Agent capability in an agent application.

## Thread Executions

Thread execution history is available from a thread's `/executions` endpoint.
Live thread state reports `activeExecution`; historical execution events never
recreate live ownership. See the core
[thread execution lifecycle](../HPD-Agent/Session/THREAD_EXECUTION_LIFECYCLE.md).
