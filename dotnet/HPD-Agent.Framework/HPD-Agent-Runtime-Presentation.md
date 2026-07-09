# HPD-Agent Runtime Architecture

## Slide 1 — Title
**HPD-Agent Runtime Architecture**

- Middleware-driven runtime for tool-using AI agents
- Built on Microsoft.Extensions.AI
- Supports chat, realtime, sessions, threads, and branching history

**Speaker notes:**
This framework is not just a chat wrapper. It is the orchestration layer that runs agent behavior end to end.

---

## Slide 2 — What the Runtime Is
- Owns the execution loop for an agent run
- Tracks session and thread state explicitly
- Coordinates model calls, tool calls, and event persistence
- Supports nested and multi-agent execution

**Speaker notes:**
The runtime is responsible for orchestration, not just inference.

---

## Slide 3 — The Core Loop
1. Receive user input
2. Build a turn context
3. Run middleware
4. Call the model
5. Process tool calls and results
6. Persist events
7. Repeat until the turn completes

**Speaker notes:**
Think of this as a state machine combined with a middleware pipeline and a model/tool execution loop.

---

## Slide 4 — Transport-Neutral Model Turns
- The runtime supports multiple transports
- Chat transport for standard LLM calls
- Realtime transport for streaming and interactive sessions
- Normalized update types unify output handling
- Model output can include text, reasoning, audio, transcripts, tools, and lifecycle events

**Speaker notes:**
The runtime abstracts provider differences so the rest of the system stays consistent.

---

## Slide 5 — Middleware-Driven Design
- Middleware hooks exist at runtime, turn, iteration, and function levels
- Middleware can inject context, enforce permissions, handle errors, and modify prompts
- Streaming hooks allow interception of model output
- Execution order is defined: before, wrap, after, and error handling

**Speaker notes:**
Middleware is the extension mechanism. It keeps the core runtime small and composable.

---

## Slide 6 — Session and Thread State
- A session contains threads
- Threads are event-sourced
- The projector rebuilds thread state from event history
- Messages, tool calls, middleware state, and compaction are reconstructed from events

**Speaker notes:**
This gives replayability, debugging, durable history, and branch-aware state management.

---

## Slide 7 — Fork Graphs and Branching
- `Thread.ForkedFrom` captures direct lineage
- `ThreadForkGraph.BuildVisibleForkGroups(...)` creates user-visible fork groups
- Fork groups represent semantic choice points
- This supports branching conversation navigation

**Speaker notes:**
The runtime separates raw lineage from meaningful user-facing forks.

---

## Slide 8 — Chat vs Realtime Execution
- `ChatModelTurnExecutor` runs standard chat turns
- `RealtimeModelTurnExecutor` manages long-lived interactive sessions
- Realtime execution can stream partial transcripts, audio, and tool outputs
- Tool results can be submitted back into the same live session

**Speaker notes:**
Chat is the simple path. Realtime is for richer, continuous interaction.

---

## Slide 9 — Why This Architecture Matters
- Replayable
- Branchable
- Observable
- Composable
- Provider-agnostic
- Safe to extend through middleware and events

**Speaker notes:**
This architecture is especially good for debugging and multi-step agent workflows.

---

## Slide 10 — Closing
- HPD-Agent Runtime = execution loop + middleware pipeline + event-sourced state
- It turns LLMs into structured, tool-using systems
- Its algorithm is built for persistence, branching, and extensibility

**Speaker notes:**
The runtime is the infrastructure that makes agent behavior reliable and manageable.
