# HPD Agent Headless UI

Framework-neutral thread state primitives for HPD-Agent UI.

This package is intentionally plain TypeScript. Framework adapters such as Svelte or React should wrap these primitives rather than live in the core.

See [PROPOSAL.md](./PROPOSAL.md) for the restart architecture and [docs/USER_DX.md](./docs/USER_DX.md) for API usage guidance.

## First Slice

The initial implementation is thread-native:

- `createThreadProjection()` folds thread snapshots and live events into UI-ready state.
- `loadThreadSnapshot()` loads durable thread state through `hpd-agent-client`.
- `createThreadController()` combines resource loading, scoped live connection, projection, and response helpers.
- `eventBelongsToScope()` guards projection so live events cannot drift into the wrong thread.

## Usage

```ts
import { AgentClient } from '@hpd-research/hpd-agent-client';
import { createThreadController } from '@hpd-research/hpd-agent-headless-ui';

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
  render(snapshot.messages, {
    streaming: snapshot.streaming,
    pendingPermissions: snapshot.pendingPermissions,
    canSend: snapshot.canSend,
  });
});

await thread.start({ includeRuns: true });
await thread.sendText('Hello');

unsubscribe();
await thread.dispose();
```

Rehydration and projection stay separate on purpose. Rehydration loads durable baseline state; projection applies live thread events after that baseline.

Live events are scoped strictly by default. If an older transport path emits scope-less events, opt into that compatibility behavior explicitly with `allowScopeLessEvents: true`.

Controllers assume a dedicated client connection by default. For a caller-owned client, pass `stopClientOnDisconnect: false` so `disconnect()` detaches listeners without stopping the shared client.
