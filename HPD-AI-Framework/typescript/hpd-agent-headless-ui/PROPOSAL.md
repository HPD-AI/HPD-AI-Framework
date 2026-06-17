# HPD Agent Headless UI Restart Proposal

## Summary

Restart `hpd-agent-headless-ui` as a small, thread-native TypeScript UI-state layer over the existing HPD Agent protocol. This is the first version of the restarted core, not a retrofit of the archived Svelte MVP.

The previous design tried to own too much: sessions, threads, thread caches, streaming, rehydration, permission state, active selection, and UI projection all lived inside one broad workspace abstraction. That made the UI layer compete with the lower-level infrastructure instead of reflecting it.

The new design should not be a second runtime. It should be a thin set of composable primitives that understand the lower-level architecture:

```text
Session = shared container
Thread = durable event stream / aggregate boundary
Thread messages = rehydrated projection
Live events = incremental continuation of a thread stream
```

The library should be a thread-native state lens for HPD Agent. The core should be framework-neutral TypeScript. Svelte, React, Vue, Solid, or other framework bindings should be adapters around the same core, not the core itself.

## Goals

- Treat `Thread` as the primary UI runtime identity.
- Separate rehydration from live event projection.
- Scope every live projection to `{ agentId, sessionId, threadId }`.
- Reuse `hpd-agent-client` for transport, REST, typed events, and request response routing.
- Keep session and thread navigation as optional helpers, not part of the core streaming primitive.
- Provide framework-neutral TypeScript state primitives without hiding the protocol lifecycle.
- Keep UI framework adapters separate from the thread/event core.
- Make disposal and scope changes explicit.

## Non-Goals

- Do not build a new event bus.
- Do not build a new transport abstraction.
- Do not duplicate thread run ownership or conflict logic.
- Do not own durable persistence.
- Do not globally cache every thread as a hidden workspace runtime.
- Do not route live events into "whatever thread is active right now."
- Do not replace `hpd-agent-client`.
- Do not depend on Svelte runes, stores, React hooks, signals, DOM APIs, or component lifecycle inside the core package.

## Existing Lower-Level Infrastructure

The lower layers already provide most of what the UI needs.

`HPD-Agent` and hosting provide:

- Durable thread event documents.
- Thread projection into messages.
- Thread-scoped live runtime instances.
- Thread run lifecycle events.
- Request session waiters and lifecycle events.
- Session-scoped and thread-scoped state separation.

`hpd-agent-client` provides:

- Typed `AgentEvent` envelopes.
- `AgentClient.on(...)` and `AgentClient.onAny(...)`.
- SSE and WebSocket transports.
- Input submission.
- Permission, clarification, continuation, and client-tool response envelopes.
- REST APIs for sessions, threads, thread events, thread messages, thread runs, and agents.

The new headless UI should compose these capabilities rather than abstracting over them as if they did not exist.

## Key Concepts

### Framework-Neutral Core

The core library should be plain TypeScript. It should work in any JavaScript runtime that can use `hpd-agent-client`.

The core should expose:

- immutable snapshots or readonly state views;
- explicit methods such as `rehydrate`, `project`, `connect`, `dispose`;
- subscriptions for state changes;
- no component lifecycle assumptions;
- no Svelte `$state`, no React hooks, no DOM dependencies.

Framework packages can adapt that core into each framework's preferred reactive model.

```text
@hpd/agent-headless-ui          -> framework-neutral core
@hpd/agent-headless-ui-svelte   -> Svelte adapter
@hpd/agent-headless-ui-react    -> React adapter
```

The archived library mixed core state, Svelte reactivity, and component behavior together. The restart should separate them.

### Thread Scope

Most live UI state belongs to a single thread runtime scope:

```ts
interface ThreadScope {
  agentId: string;
  sessionId: string;
  threadId: string;
}
```

A live stream, projection, pending permissions, and thread run state should all be tied to one `ThreadScope`.

### Rehydration

Rehydration loads durable thread state that already happened.

Possible inputs:

```ts
Thread
ThreadMessage[]
ThreadEvent[]
ThreadRun[]
```

Rehydration establishes a settled baseline. It should not simulate streaming. Rehydrated messages should be non-streaming, non-thinking, and stable.

### Projection

Projection applies individual live events to UI state.

Input:

```ts
AgentEvent
```

Output:

```ts
messages
streaming
reasoning
activeTools
pendingPermissions
pendingClarifications
threadRun
error
```

Projection should be deterministic, mostly pure in behavior, and transport-agnostic.

### Live Stream

Live stream connects to the backend for exactly one thread scope. It receives runtime events and feeds them into a projection.

The live stream is not the durable history. It is the continuation of the thread after the current baseline.

## Core Rule

Never route live events into a global active state.

Avoid:

```ts
client.onAny((event) => activeState.dispatch(event));
```

Prefer:

```ts
client.onAny((event) => {
  if (belongsToScope(event, scope)) {
    projection.project(event);
  }
});
```

Or use a thread controller whose transport connection is already scoped to the thread.

## Proposed Public API

Start with a very small framework-neutral package surface.

```ts
export {
  createThreadProjection,
  createThreadController,
  loadThreadSnapshot,
  eventBelongsToScope,
};
```

Session list, thread list, and thread navigation helpers can be added after these primitives are solid. UI components belong in framework adapter packages.

## Primitive 1: `createThreadProjection`

`createThreadProjection` owns state for one thread. It does not fetch, connect, submit, or respond. It only rehydrates and projects.

```ts
const projection = createThreadProjection();

projection.rehydrate({
  thread,
  messages,
  runs,
});

projection.project(event);
```

### Responsibilities

- Hold message state.
- Hold live streaming flags.
- Hold active tool calls.
- Hold pending permissions.
- Hold pending clarifications.
- Hold pending client tool requests if the app wants to render them.
- Hold current thread run summary.
- Apply live events.
- Load durable snapshots.
- Reset state.

### Non-Responsibilities

- No transport connection.
- No HTTP calls.
- No session selection.
- No thread selection.
- No global thread cache.
- No hidden active-scope tracking.

### Suggested Type

```ts
interface ThreadProjection {
  getSnapshot(): ThreadProjectionSnapshot;
  subscribe(listener: ThreadProjectionListener): Unsubscribe;

  rehydrate(snapshot: ThreadSnapshot): void;
  project(event: AgentEvent): void;
  clearError(): void;
  reset(): void;
}

interface ThreadProjectionSnapshot {
  messages: Message[];
  streaming: boolean;
  reasoning: boolean;
  activeTools: ToolCall[];
  pendingPermissions: PermissionRequest[];
  pendingClarifications: ClarificationRequest[];
  threadRun: ThreadRunView | null;
  error: string | null;
  canSend: boolean;
}

type ThreadProjectionListener = (snapshot: ThreadProjectionSnapshot) => void;
type Unsubscribe = () => void;
```

### Rehydration Inputs

```ts
interface ThreadSnapshot {
  thread?: Thread | null;
  messages?: ThreadMessage[];
  events?: ThreadEvent[];
  runs?: ThreadRun[];
  activeRun?: ThreadRun | null;
}
```

The initial implementation should support `messages` first because it is the stable projected view the backend already provides.

Later, `events` can be supported for canonical replay when UI needs richer event-level state.

### Event Mapping

Minimum live mappings:

```text
TEXT_MESSAGE_START       -> create/update assistant message, streaming true
TEXT_DELTA               -> append text to message
TEXT_MESSAGE_END         -> mark message not streaming
REASONING_MESSAGE_START  -> create/update message reasoning state
REASONING_DELTA          -> append reasoning text
REASONING_MESSAGE_END    -> mark reasoning false
TOOL_CALL_START          -> add active tool
TOOL_CALL_ARGS           -> update tool args
TOOL_CALL_RESULT         -> complete tool with result
TOOL_CALL_END            -> complete tool if not already completed
PERMISSION_REQUEST       -> add pending permission
PERMISSION_APPROVED      -> remove pending permission
PERMISSION_DENIED        -> remove pending permission
CLARIFICATION_REQUEST    -> add pending clarification
MESSAGE_TURN_STARTED     -> mark streaming/running context
MESSAGE_TURN_FINISHED    -> clear turn context
MESSAGE_TURN_ERROR       -> set error, clear active streaming flags
THREAD_RUN_STARTED       -> mark thread run active
THREAD_RUN_COMPLETED     -> mark thread run complete/cancelled/failed
```

Unknown events should be ignored by default but optionally observable.

## Primitive 2: `createThreadController`

`createThreadController` combines a thread projection with client commands and a scoped live connection.

```ts
const thread = createThreadController({
  client,
  agentId,
  sessionId,
  threadId,
});

await thread.rehydrate();
await thread.connect();
await thread.sendText("hello");
await thread.approve(permissionId);
await thread.dispose();
```

### Responsibilities

- Own exactly one `ThreadScope`.
- Own one `ThreadProjection`.
- Rehydrate durable state for its thread.
- Connect and disconnect the live stream.
- Submit user input to the scoped thread.
- Send request responses for permission and clarification events.
- Interrupt active thread work.
- Dispose subscriptions and network connections.

### Non-Responsibilities

- No session list.
- No thread list.
- No thread switching.
- No global thread cache.
- No app-level navigation policy.

### Suggested Type

```ts
interface ThreadController {
  readonly scope: ThreadScope;
  readonly projection: ThreadProjection;

  readonly connected: boolean;
  readonly loading: boolean;
  readonly error: string | null;

  rehydrate(options?: RehydrateOptions): Promise<void>;
  connect(options?: ConnectOptions): Promise<void>;
  disconnect(): Promise<void>;
  dispose(): Promise<void>;

  sendText(text: string, options?: SendTextOptions): Promise<void>;
  run(input: AgentRunInputEvent): Promise<void>;
  interrupt(options?: InterruptOptions): Promise<void>;

  approve(permissionId: string, choice?: PermissionChoice): Promise<void>;
  deny(permissionId: string, reason?: string): Promise<void>;
  clarify(requestId: string, answer: string): Promise<void>;
}
```

### Lifecycle

The normal lifecycle should be explicit:

```text
create controller
  -> rehydrate durable baseline
  -> connect live thread stream
  -> send input
  -> project live events
  -> thread run completes
  -> optionally refresh durable state
  -> disconnect/dispose
```

The controller may offer a convenience method:

```ts
await thread.start();
```

But internally that should mean:

```ts
await thread.rehydrate();
await thread.connect();
```

### Event Scope Guard

Even if the transport endpoint is thread-scoped, the controller should guard events.

```ts
if (eventBelongsToScope(event, scope)) {
  projection.project(event);
}
```

Scope-less root runtime events are a known backend behavior, but the core should not accept them by default. If an app has a scoped connection that emits older scope-less events, it can opt into that compatibility behavior explicitly.

Suggested options:

```ts
interface ScopeGuardOptions {
  allowScopeLess?: boolean;
}
```

Default for `createThreadController`: `false`.

Default for standalone `eventBelongsToScope`: `false`.

## Primitive 3: `loadThreadSnapshot`

`loadThreadSnapshot` is an optional fetch helper for durable thread data. It should not connect live streams and should not retain cached state.

```ts
const snapshot = await loadThreadSnapshot(
  { client, agentId, sessionId, threadId },
  { includeRuns: true },
);
```

This can power apps that want full control over projection and transport.

## Optional Later Helpers

These should be separate from thread streaming.

```ts
createSessionList(client)
createThreadList(client, sessionId)
createThreadNavigator(client, sessionId)
```

They can expose metadata and navigation helpers, but should not own live event projection.

## Framework Adapters

The core package should not export components. It should export state machines, controllers, snapshots, and subscriptions.

Framework adapters should consume projections/controllers rather than create hidden runtimes.

Adapter examples:

```svelte
<MessageList projection={thread.projection} />
<ChatInput controller={thread} />
<PermissionDialog controller={thread} />
<ToolCallList projection={thread.projection} />
```

```tsx
<MessageList projection={thread.projection} />
<ChatInput controller={thread} />
<PermissionDialog controller={thread} />
<ToolCallList projection={thread.projection} />
```

Framework adapters should translate the core subscription API into local reactivity:

- Svelte adapter: stores/runes/components.
- React adapter: hooks/components.
- Vue adapter: composables/components.
- Solid adapter: signals/components.

The event lifecycle remains in the core primitives.

This is what `headless-ui.framework` means in practice: the framework package is an adapter layer over the same headless TypeScript core. It provides idiomatic bindings for a UI framework without changing the underlying thread lifecycle model.

## Request Sessions

Request session response routing is already provided by the backend and client. The headless UI should only store pending request UI state and call the correct response method.

Permission flow:

```text
PERMISSION_REQUEST
  -> projection.pendingPermissions += request
  -> UI calls controller.approve(...) or controller.deny(...)
  -> controller sends PERMISSION_RESPONSE through client
  -> backend waiter resolves
```

Clarification flow:

```text
CLARIFICATION_REQUEST
  -> projection.pendingClarifications += request
  -> UI calls controller.clarify(...)
  -> controller sends CLARIFICATION_RESPONSE
```

Continuation flow can initially default to app-provided handling. Auto-approval should not be hidden in core projection.

## Client Tool Events

`hpd-agent-client` already has `ClientToolRegistry` and auto-response handling for `CLIENT_TOOL_INVOKE_REQUEST`.

Headless UI should not duplicate that registry.

It may expose pending client tool requests for display/debugging, but actual invocation should remain in the client unless a future product requirement says otherwise.

## Rehydration Strategies

### Message Rehydration

Default strategy.

```ts
const messages = await client.getThreadMessages(sessionId, threadId);
projection.rehydrate({ messages });
```

Pros:

- Fast.
- Stable.
- Already projected by backend.
- Good for chat transcript display.

Cons:

- Loses some event-level detail.
- Does not reconstruct every transient runtime state.

### Event Rehydration

Canonical strategy.

```ts
const events = await client.getThreadEvents(sessionId, threadId);
projection.rehydrate({ events });
```

Pros:

- Closer to source of truth.
- Can reconstruct richer thread UI.

Cons:

- More complex.
- Must be careful not to replay old events as if they are currently streaming.

### Hybrid Rehydration

Potential future strategy.

```ts
const [thread, messages, runs] = await Promise.all([
  client.getThread(sessionId, threadId),
  client.getThreadMessages(sessionId, threadId),
  client.getThreadRuns(agentId, sessionId, threadId),
]);
```

Use messages for transcript, runs for lifecycle, thread for metadata.

## Error Model

Separate error categories:

```ts
type ThreadControllerErrorKind =
  | "rehydration"
  | "connection"
  | "submission"
  | "runtime"
  | "response";
```

Projection errors from `MESSAGE_TURN_ERROR` are runtime errors.

Network failures during `connect`, `rehydrate`, or `sendText` are controller errors.

The UI should be able to clear projection errors without losing durable state.

## Disposal Model

Disposal must be explicit.

```ts
await thread.disconnect();
await thread.dispose();
```

`dispose()` should:

- disconnect live transport if connected;
- dispose all client subscriptions;
- stop the client connection by default, unless `stopClientOnDisconnect: false` is set for caller-owned clients;
- abort any in-flight connect/rehydrate if owned by the controller;
- prevent further projection updates;
- leave already-projected state readable if possible.

## Scope Changes

Changing thread selection is an app-shell concern.

Recommended app behavior:

```ts
let thread = createThreadController({ client, agentId, sessionId, threadId });
await thread.start();

// On thread selection change:
await thread.dispose();
thread = createThreadController({ client, agentId, sessionId, threadId: nextThreadId });
await thread.start();
```

This is intentionally boring. It avoids cross-thread stream leakage.

## Suggested Initial File Layout

```text
src/lib/
  thread/
    thread-controller.ts
    thread-projection.ts
    load-thread-snapshot.ts
    scope.ts
    types.ts
    index.ts
  internal/
    map-thread-message.ts
    map-event.ts
  index.ts
```

Do not restore the previous broad `workspace` module at first.

Suggested future adapter layout:

```text
typescript/
  hpd-agent-headless-ui/          # framework-neutral core
  hpd-agent-headless-ui-svelte/   # Svelte adapter/components
  hpd-agent-headless-ui-react/    # React adapter/components
```

## Migration From Archived Design

The archived implementation contains useful pieces:

- `AgentState` event handlers are a good starting point for `ThreadProjection`.
- `mapToUIMessages` is useful for message rehydration.
- Permission dialog behavior is useful for a future framework adapter, but should depend on a thread controller, not a workspace.
- Message, tool, input, and run-config components can be salvaged into adapter packages after the core primitives settle.

Avoid carrying forward:

- the single `WorkspaceImpl` that owns all levels;
- thread LRU cache;
- active-state global event dispatch;
- hidden continuation auto-approval;
- conflating thread navigation with thread streaming;
- treating `send()` as sufficient when no live observer is connected.

## Implementation Plan

### Phase 1: Core Thread Projection

- Add `createThreadProjection`.
- Port minimal message/tool/permission/clarification event handling.
- Add message rehydration from `ThreadMessage[]`.
- Add unit tests for event projection and rehydration.

### Phase 2: Thread Controller

- Add `createThreadController`.
- Use existing `AgentClient` for `start`, `stop`, `run`, and typed responses.
- Ensure `connect()` uses exact thread scope.
- Ensure `sendText()` connects or clearly requires connection.
- Add scope guard.
- Add disposal tests.

### Phase 3: Framework Adapter Spike

- Build a small Svelte adapter package over the framework-neutral core.
- Rebuild `MessageList`, `ChatInput`, `PermissionDialog`, and `ToolCallList` against `ThreadProjection` / `ThreadController`.
- Keep components stateless where possible.
- Prove that another adapter, such as React, would not need core changes.

### Phase 4: Optional Navigation Helpers

- Add session and thread list helpers only after thread primitives are proven.
- Keep them separate from streaming.

## Open Questions

- Should `sendText()` automatically call `connect()` if disconnected, or should the app call `connect()` explicitly?
- Should `ThreadController.start()` be the recommended happy path?
- Should event rehydration be supported in v1 of the restart, or should message rehydration ship first?
- Should scope-less events be accepted only during active runs?
- Should thread controllers share one `AgentClient`, or should each controller own its own client/transport instance by default?
- What should the adapter package naming convention be?
- Should the core package include tiny framework-agnostic DOM helpers, or should all DOM behavior live only in adapters?

## Recommended Decisions

- Ship message rehydration first.
- Ship explicit `start()` as convenience for `rehydrate + connect`.
- Let `sendText()` require a connected controller initially, or make auto-connect an explicit option:

```ts
createThreadController({ ..., autoConnectOnSend: true })
```

- Use one `AgentClient` per controller until the client exposes isolated thread stream objects. This avoids handler leakage from shared global `onAny` subscriptions.
- Keep event rehydration as a second pass once the core thread projection is stable.

## North Star

The headless UI is not the runtime. It is not the event store. It is not the thread owner.

It is a framework-neutral state lens over a thread event stream.
