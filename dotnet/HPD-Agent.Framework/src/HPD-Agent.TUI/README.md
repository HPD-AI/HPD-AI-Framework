# HPD-Agent.TUI

Terminal UI shell primitives for HPD agents.

## Install

```bash
dotnet add package HPD-Agent.TUI
```

## Use When

Use this package when you need this HPD Agent capability in an agent application.

## Pre-1.0 API Evolution

HPD-Agent.TUI is still pre-1.0. Until `1.0.0`, releases may refine runtime
interfaces, transcript rendering contracts, and model-selection surfaces as the
terminal experience stabilizes. The current TUI model is session/thread runtime
navigation with transcript cells and renderer registrations.

## Thread Execution State

The TUI may project `THREAD_EXECUTION_STARTED` and
`THREAD_EXECUTION_FINISHED` for historical presentation. Present-tense busy and
cancel state comes from the runtime's authoritative `activeExecution`, so replay
cannot make an idle thread appear active. See the core
[thread execution lifecycle](../HPD-Agent/Session/THREAD_EXECUTION_LIFECYCLE.md).
