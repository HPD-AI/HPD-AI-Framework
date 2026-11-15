# HPD Agent Headless UI

Framework-neutral thread UI primitives for HPD Agent.

This package is plain TypeScript. Framework adapters such as Svelte or React
wrap these primitives instead of living in the core.

See [PROPOSAL.md](./PROPOSAL.md) for the architecture and
[docs/USER_DX.md](./docs/USER_DX.md) for API usage guidance. See
[Extensions DX](./docs/EXTENSIONS_DX.md) for application and
runtime-extension event projection.

## First Slice

The implementation is thread-native and timeline-first:

- `createThreadProjection()` folds durable snapshots and live events into UI
  timeline state.
- `loadThreadSnapshot()` loads durable thread state through `hpd-agent-client`.
- `createThreadController()` combines loading, scoped live connection,
  projection, input submission, and runtime request response helpers.
- `createThreadBranchNavigator()` loads graph-derived fork-group and
  child-thread navigation metadata without owning live state.
- `eventBelongsToScope()` guards projection so live events cannot drift into the
  wrong thread.
- `extensions` let an application reduce runtime-extension and application-owned
  events into typed state without regenerating the SDK.

## Usage

```ts
import { AgentClient } from '@hpd-research/hpd-agent-client';
import {
  createThreadController,
  getPendingRuntimeRequests,
  getThreadTimeline,
  getThreadWorkGroups,
  getTranscriptMessages,
} from '@hpd-research/hpd-agent-headless-ui';

const client = new AgentClient({
  baseUrl: 'http://localhost:5000',
});

const thread = createThreadController({
  client,
  agentId: 'agent-1',
  sessionId: 'session-1',
  threadId: 'thread-1',
});

const unsubscribe = thread.projection.subscribe((snapshot) => {
  render({
    timeline: getThreadTimeline(snapshot),
    workGroups: getThreadWorkGroups(snapshot),
    transcriptMessages: getTranscriptMessages(snapshot),
    activity: snapshot.activity,
    pendingRuntimeRequests: getPendingRuntimeRequests(snapshot),
    canSend: snapshot.canSend,
  });
});

await thread.start({ includeExecutions: true });
await thread.sendMessage({ contents: [{ $type: 'text', text: 'Hello' }] });

unsubscribe();
await thread.dispose();
```

## Application Events

Application-specific events remain application-owned. Register a reducer when
the thread projection is created; it receives known runtime-extension envelopes
and unknown event types through the same replay/live path.

```ts
const analysis = {
  key: 'acme:analysis-progress@1',
  initialState: { percent: 0 },
  matches: (event) => event.type === 'RUNTIME_AGENT_DURABLE_EVENT'
    || event.type === 'RUNTIME_AGENT_LIVE_EVENT',
  reduce: (state, event) => {
    const value = event as Record<string, unknown>;
    if (value.extensionNamespace !== 'acme'
      || value.schemaName !== 'analysis-progress') return state;
    const payload = value.payload as { percent: number };
    return { percent: payload.percent };
  },
} satisfies ThreadEventExtension<{ percent: number }>;

const thread = createThreadController({
  client,
  agentId: 'agent-1',
  sessionId: 'session-1',
  threadId: 'thread-1',
  extensions: [analysis],
});

const state = thread.projection.getSnapshot().extensions;
```

Extensions are reset with the projection and run again during durable replay,
including after a hard journal rebase. `LIVE_EVENT` payloads are transient;
`DURABLE_EVENT` payloads reconstruct extension state after reconnect.

Rehydration and projection stay separate on purpose. Rehydration loads the
durable baseline, including answerable pending requests; projection applies live
thread events after that baseline. Durable request terminal events remove stale
controls automatically.

Permission and clarification helpers return `RespondResult`, so expected races
such as `alreadyResolved`, `cancelled`, and `executionEnded` remain ordinary UI
state rather than thrown transport failures.

Live events are scoped strictly by default. If an older transport emits
scope-less events, opt into that behavior explicitly with
`allowScopeLessEvents: true`.

Each thread controller owns its observation. `disconnect()` closes that
observation while leaving the shared `AgentClient` available for other threads.
