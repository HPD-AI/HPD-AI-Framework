# HPD Agent Headless UI

Framework-neutral thread UI primitives for HPD Agent.

This package is plain TypeScript. Framework adapters such as Svelte or React
wrap these primitives instead of living in the core.

See [PROPOSAL.md](./PROPOSAL.md) for the architecture and
[docs/USER_DX.md](./docs/USER_DX.md) for API usage guidance.

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
  transport: 'sse',
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

await thread.start({ includeRuns: true });
await thread.sendMessage({ contents: [{ $type: 'text', text: 'Hello' }] });

unsubscribe();
await thread.dispose();
```

Rehydration and projection stay separate on purpose. Rehydration loads the
durable baseline; projection applies live thread events after that baseline.

Live events are scoped strictly by default. If an older transport emits
scope-less events, opt into that behavior explicitly with
`allowScopeLessEvents: true`.

Controllers assume a dedicated client connection by default. For a caller-owned
client, pass `stopClientOnDisconnect: false` so `disconnect()` detaches
listeners without stopping the shared client.
