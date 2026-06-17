# User DX Guide

This package is the framework-neutral branch UI core for HPD Agent. It is meant to be small, explicit, and easy to wrap from Svelte, React, Vue, Solid, or a custom renderer.

The normal user-facing path is `createBranchController`. The lower-level primitives are exported for adapter authors and advanced integrations.

## Mental Model

```text
Session = shared container
Branch = durable event stream / runtime scope
Branch messages = durable transcript baseline
Live events = incremental updates after the baseline
```

The core rule is simple: a controller represents one branch scope.

```ts
{
  agentId: string;
  sessionId: string;
  branchId: string;
}
```

Do not route events into "the active branch." Route events into the branch they belong to.

## Happy Path

Use `createBranchController` when building a chat surface or framework adapter.

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
  render({
    messages: snapshot.messages,
    streaming: snapshot.streaming,
    reasoning: snapshot.reasoning,
    pendingPermissions: snapshot.pendingPermissions,
    pendingClarifications: snapshot.pendingClarifications,
    canSend: snapshot.canSend,
    error: snapshot.error,
  });
});

await branch.start({ includeRuns: true });
await branch.sendText('Hello');

unsubscribe();
await branch.dispose();
```

`start()` is intentionally explicit composition:

```text
load durable branch snapshot
  -> rehydrate projection
  -> connect scoped live stream
  -> project matching live events
```

## API Layers

`createBranchController()` is the main headless API. It owns one branch lifecycle: rehydrate, connect, send input, respond to runtime requests, interrupt, disconnect, dispose.

`createBranchProjection()` is the state fold. It does not fetch, connect, or submit anything. Use this when you want to manually feed snapshots and events.

`loadBranchSnapshot()` is the durable loader. It calls the existing client REST APIs and returns a plain snapshot. It does not cache state.

`eventBelongsToScope()` is the guard for branch-safe event routing.

## Rehydration vs Projection

Rehydration loads what already happened.

```ts
const snapshot = await loadBranchSnapshot(
  { client, agentId, sessionId, branchId },
  { includeRuns: true },
);

projection.rehydrate(snapshot);
```

Projection applies what is happening now.

```ts
client.onAny((event) => {
  if (eventBelongsToScope(event, scope)) {
    projection.project(event);
  }
});
```

Do not replay durable messages as streaming deltas. Durable messages are the baseline. Live events are the continuation.

## Sending Input

`sendText()` sends exactly the text passed to it.

```ts
await branch.sendText('Summarize this branch');
```

Use `run()` for lower-level protocol input.

```ts
await branch.run({
  type: 'USER_MESSAGES_INPUT',
  messages: [{ role: 'user', content: 'Hello' }],
});
```

The controller stamps missing `agentId`, `sessionId`, and `branchId` onto input events. It does not mutate message content for attachments. File upload state and content-reference formatting belong in an input/file adapter above this core.

## Runtime Requests

The projection exposes pending runtime requests:

```ts
snapshot.pendingPermissions
snapshot.pendingClarifications
snapshot.pendingClientToolRequests
```

The controller provides response helpers for common user-mediated requests:

```ts
const result = await branch.approve(permissionId);
await branch.deny(permissionId, 'Not allowed');
await branch.clarify(requestId, 'Use the production tenant');
```

These helpers send responses through the client and may return a structured request-session status:

```ts
if (result?.status === 'alreadyResolved') {
  // Another observer answered first; the lifecycle event will remove the stale prompt.
}
```

The lower runtime still owns whether work continues and how branch-run state changes. Pending request UI should clear from `AGENT_REQUEST_RESOLVED`, `AGENT_REQUEST_EXPIRED`, or `AGENT_REQUEST_CANCELLED`; raw response payloads are not the generic cleanup contract.

## Interrupting Work

`interrupt()` expresses user intent to stop active work.

```ts
await branch.interrupt({ reason: 'User cancelled' });
```

The UI should wait for branch-run events or a later rehydration to confirm final status. Do not treat the interrupt call itself as durable lifecycle truth.

## Connection Ownership

By default, a controller assumes it owns a dedicated `AgentClient` connection.

```ts
await branch.disconnect(); // stops the client and detaches listeners
```

If a caller owns a shared client, opt out of stopping the client:

```ts
const branch = createBranchController({
  client,
  agentId,
  sessionId,
  branchId,
  stopClientOnDisconnect: false,
});
```

Then `disconnect()` detaches this controller's listeners without stopping the shared transport.

## Strict Scope Defaults

Live event projection is strict by default.

```ts
allowScopeLessEvents: false
```

Events with mismatched `agentId`, `sessionId`, or `branchId` are ignored. Scope-less events are ignored unless explicitly enabled for compatibility:

```ts
const branch = createBranchController({
  client,
  agentId,
  sessionId,
  branchId,
  allowScopeLessEvents: true,
});
```

Only enable this for an older scoped transport path that cannot emit branch fields yet.

## Framework Adapter Shape

A framework adapter should be thin. It should wrap the controller's subscription model into the framework's reactive model.

For example, a Svelte adapter might:

- create a `BranchController`;
- subscribe to `branch.projection`;
- expose reactive `messages`, `streaming`, `pendingPermissions`, and `canSend`;
- call `branch.sendText`, `branch.approve`, `branch.deny`, `branch.clarify`, and `branch.interrupt`;
- call `await branch.dispose()` from framework cleanup.

It should not:

- create a second event bus;
- keep a hidden global active branch runtime;
- cache every branch as part of the streaming primitive;
- mutate user text to smuggle attachment references;
- make branch switching implicit.

## Branch Switching

Branch switching is an app-shell concern.

```ts
await currentBranch.dispose();

currentBranch = createBranchController({
  client,
  agentId,
  sessionId,
  branchId: nextBranchId,
});

await currentBranch.start({ includeRuns: true });
```

This boring lifecycle is intentional. It keeps event routing obvious and prevents cross-branch stream leakage.

## What This Core Does Not Own

The core does not own durable persistence, branch-run scheduling, session navigation, branch navigation, file upload UX, framework components, or app-level layout.

It observes and projects branch state. It sends user/runtime responses through `hpd-agent-client`. The lower HPD Agent runtime remains the source of truth.
