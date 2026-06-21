# HPD Agent Headless UI Proposal

## Summary

`hpd-agent-headless-ui` is a small, thread-native TypeScript UI-state layer over
the HPD Agent protocol. It is not a port of the archived Svelte MVP and it does
not preserve the old flat message-list contract.

The package projects one thread into a UI read model:

```text
Session = shared container
Thread = durable event stream / aggregate boundary
Transcript = final user-visible message leaves
Work = live or completed turn activity
Timeline = ordered transcript leaves and work groups
Runtime request = standardized request/response envelope
```

The key break is deliberate: the projection does not expose
`snapshot.messages`, `snapshot.streaming`, or `snapshot.reasoning`. Those fields
were too small for real agent lifecycle UI. The official contract is
`timeline`, `workGroups`, `transcriptMessages`, `activity`,
`activeTools`, and `pendingRuntimeRequests`.

## Goals

- Treat `Thread` as the primary UI runtime identity.
- Use one projection path for durable rehydration and live streaming events.
- Scope every live projection to `{ agentId, sessionId, threadId }`.
- Reuse `hpd-agent-client` for transport, REST, typed events, read-model
  helpers, and response routing.
- Represent turn lifecycle as work groups instead of forcing everything into
  transcript messages.
- Standardize request/response envelopes so built-in and custom request events
  can be surfaced consistently.
- Keep session and thread navigation as optional helpers, not part of the live
  controller.
- Keep the core framework-neutral and DOM-free.
- Make disposal and scope changes explicit.

## Non-Goals

- Do not build a new event bus.
- Do not build a new transport abstraction.
- Do not duplicate thread run ownership or conflict logic.
- Do not own durable persistence.
- Do not globally cache every thread as a hidden workspace runtime.
- Do not route live events into "whatever thread is active right now."
- Do not replace `hpd-agent-client`.
- Do not depend on Svelte runes, stores, React hooks, signals, DOM APIs, or
  component lifecycle inside the core package.
- Do not preserve the early Svelte adapter's deleted `ThreadMessages` contract.

## Existing Lower-Level Infrastructure

The lower layers already provide most of what the UI needs.

`HPD-Agent` and hosting provide:

- durable thread event documents;
- thread-scoped live runtime instances;
- thread run lifecycle events;
- request session waiters and lifecycle events;
- session-scoped and thread-scoped state separation.

`hpd-agent-client` provides:

- typed `AgentEvent` envelopes;
- `AgentClient.on(...)` and `AgentClient.onAny(...)`;
- SSE and WebSocket transports;
- input submission;
- permission, clarification, continuation, and client-tool response envelopes;
- REST APIs for sessions, threads, thread events, thread messages, thread runs,
  and agents;
- protocol/read-model helpers for reconstructing durable transcript messages and
  formatting tool results.

Headless UI composes those capabilities. It should not hide them behind a second
runtime.

## Core Shape

```ts
interface ThreadProjectionSnapshot {
  timeline: ThreadTimelineItem[];
  workGroups: ThreadWorkGroup[];
  transcriptMessages: Message[];
  activeTools: ToolCall[];
  pendingRuntimeRequests: RuntimeRequest[];
  threadRun?: ThreadRun;
  activity: ThreadActivity;
  currentTurnId?: string;
  currentConversationId?: string;
  currentRunId?: string;
  error?: Error;
  canSend: boolean;
}
```

This shape supports the UI that modern agent products need:

- render only the final transcript;
- show live reasoning and draft text while a turn is running;
- group many tool calls into one compact work row;
- collapse completed work after the final assistant message lands;
- expose custom runtime requests without creating new one-off component APIs;
- let applications choose density and grouping policy.

## Timeline Items

The timeline is the primary conversation read model.

```ts
type ThreadTimelineItem =
  | { kind: 'message'; message: Message }
  | { kind: 'work'; workGroup: ThreadWorkGroup }
  | { kind: 'runtime-request'; request: RuntimeRequest };
```

The exact union can grow, but the important rule stays the same: the projection
preserves lifecycle structure and the renderer decides how to display it.

## Work Groups

`ThreadWorkGroup` models a turn while it is happening and after it completes.
It can contain:

- reasoning parts;
- assistant draft text;
- tool calls;
- tool results;
- runtime request markers;
- status/error metadata.

When the turn finishes, the projection can collapse the work group and promote
the final assistant text into `transcriptMessages`. That lets UIs show the
interesting live activity during execution without permanently expanding every
implementation detail in the transcript.

## Runtime Requests

Runtime requests are standardized envelopes instead of separate ad hoc buckets.

```ts
request.id
request.kind
request.sourceName
request.requestEventType
request.expectedResponseEventType
request.responsePolicy
request.target
request.visibility
```

Known request kinds receive typed payloads. Custom request events remain visible
as custom envelopes. This matters because HPD users can create their own request
and response events; the UI layer should not require a library release for every
new request type.

Controller helpers exist for common responses:

```ts
await thread.approve(permissionId);
await thread.deny(permissionId, 'Not allowed');
await thread.clarify(requestId, 'Use the production tenant');
await thread.respondToClientTool(requestId, result);
```

Custom response events use the generic response path:

```ts
await thread.respond({
  type: 'CUSTOM_RESPONSE_EVENT',
  requestId,
  sourceName,
  value,
});
```

The projection clears pending request UI from lifecycle events such as resolved,
expired, and cancelled. Raw response payloads are not the generic cleanup
contract.

## API Layers

`createThreadController()` is the main headless API. It owns one thread
lifecycle: load, connect, project, submit input, respond to runtime requests,
interrupt, disconnect, and dispose.

`createThreadProjection()` is the state fold. It does not fetch, connect, or
submit anything.

`loadThreadSnapshot()` loads the durable thread event stream from the client and
returns a plain snapshot.

`createThreadBranchNavigator()` loads graph-derived fork-group and child-thread
metadata. It is not a live runtime object and it does not mutate controller
scope.

`eventBelongsToScope()` guards live event routing.

Selectors provide pure read models over snapshots and branch navigation metadata.

## Rehydration

Rehydration loads durable state that already happened. The current baseline is
the durable thread event stream plus optional runs and metadata.

```ts
const snapshot = await loadThreadSnapshot(
  { client, agentId, sessionId, threadId },
  { includeRuns: true },
);

projection.rehydrate(snapshot);
```

Durable events are the input format. The projection replays them through the
same fold used for live events, muting intermediate emissions and emitting one
settled snapshot at the end. That makes refresh, reconnect, and live streaming
produce the same `timeline`, `workGroups`, and `transcriptMessages`.

Durable thread messages remain a lower-client read model and can be used by
specialized helpers such as revision targeting, but they are not the hydration
contract for headless UI.

## Live Projection

Live events update the current thread's work, transcript, activity, tools,
requests, and errors.

```ts
client.onAny((event) => {
  if (eventBelongsToScope(event, scope)) {
    projection.project(event);
  }
});
```

Projection is strict by default. Scope-less events are ignored unless the caller
opts into compatibility behavior with `allowScopeLessEvents: true`.

## Svelte Adapter Direction

The Svelte package should mirror the core shape rather than inventing another
runtime:

```ts
interface ThreadStateSnapshot {
  projection: ThreadProjectionSnapshot;
  timeline: ThreadTimelineItem[];
  workGroups: ThreadWorkGroup[];
  transcriptMessages: Message[];
  activeTools: ToolCall[];
  pendingRuntimeRequests: RuntimeRequest[];
  activity: ThreadActivity;
  ...
}
```

Keep focused leaf/control components:

- `Message`
- `ThreadComposer`
- `ThreadStatus`
- `RuntimeRequest`
- `ThreadRuntimeRequests`

Build next:

- `ThreadTimeline`
- `ThreadWorkGroup`
- tool/work render snippets

Remove or rebuild:

- `ThreadMessages` as a primary architecture. If a transcript-only helper comes
  back, it should be a thin convenience over `transcriptMessages`, not the core
  conversation model.

## Second-Mover Advantage

VS Code Copilot Chat and similar agent UIs show the pain of incremental
evolution: transcript rendering, tool activity, progress, request prompts, and
collapse policy often become separate patched surfaces. HPD can start from the
lesson instead of repeating the path.

The headless core should expose the lifecycle model directly. Users then get
control over presentation:

- render one tool call or group twenty;
- keep work expanded or collapse it at turn end;
- show only the transcript or full execution detail;
- render custom request kinds without waiting on framework components;
- place timeline parts in chat, sidebars, inspectors, or debug panes.

This is why the break is worth it now. The library is early, the archive is the
old path, and no compatibility contract should constrain the timeline-first
design.
