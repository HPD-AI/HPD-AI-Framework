# HPD Agent Headless UI

Framework-neutral branch state primitives for HPD-Agent UI.

This package is intentionally plain TypeScript. Framework adapters such as Svelte or React should wrap these primitives rather than live in the core.

See [PROPOSAL.md](./PROPOSAL.md) for the restart architecture and [docs/USER_DX.md](./docs/USER_DX.md) for API usage guidance.

## First Slice

The initial implementation is branch-native:

- `createBranchProjection()` folds branch snapshots and live events into UI-ready state.
- `loadBranchSnapshot()` loads durable branch state through `hpd-agent-client`.
- `createBranchController()` combines resource loading, scoped live connection, projection, and response helpers.
- `eventBelongsToScope()` guards projection so live events cannot drift into the wrong branch.

## Usage

```ts
import { AgentClient } from '@hpd-research/hpd-agent-client';
import { createBranchController } from '@hpd-research/hpd-agent-headless-ui';

const client = new AgentClient({
  baseUrl: 'http://localhost:5000',
  transport: 'sse',
});

const branch = createBranchController({
  client,
  agentId: 'agent-1',
  sessionId: 'session-1',
  branchId: 'branch-1',
});

const unsubscribe = branch.projection.subscribe((snapshot) => {
  render(snapshot.messages, {
    streaming: snapshot.streaming,
    pendingPermissions: snapshot.pendingPermissions,
    canSend: snapshot.canSend,
  });
});

await branch.start({ includeRuns: true });
await branch.sendText('Hello');

unsubscribe();
await branch.dispose();
```

Rehydration and projection stay separate on purpose. Rehydration loads durable baseline state; projection applies live branch events after that baseline.

Live events are scoped strictly by default. If an older transport path emits scope-less events, opt into that compatibility behavior explicitly with `allowScopeLessEvents: true`.

Controllers assume a dedicated client connection by default. For a caller-owned client, pass `stopClientOnDisconnect: false` so `disconnect()` detaches listeners without stopping the shared client.
