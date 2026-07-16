# HPD-Agent TypeScript Client SDK

TypeScript SDK for building HPD-Agent chat/runtime applications.

## Features

- **Chat runtime** - Open chat sessions, load thread history, and send text turns.
- **Client tools** - Register browser/client-side tools with automatic response events.
- **Resumable committed events** - SSE reconnects from the last event successfully applied by the consumer.
- **Authoritative lifecycle** - One thread-state snapshot contains history, cursor, and the active backend-owned run.
- **Safe interruption** - Cancellation compares the expected run ID and returns a structured lifecycle result.
- **Type safe protocol** - Typed agent events, session/thread DTOs, run config, client tools, and eval DTOs.
- **Zero runtime dependencies** - Pure TypeScript for browser and Node.js.

## Quick Start

```typescript
import { AgentClient } from '@hpd-research/hpd-agent-client';

const client = new AgentClient({ baseUrl: 'http://localhost:5135' });

client.tools.register('get_active_view', () => ({
  activeView: 'chat',
}));

const chat = await client.chat.open({
  agentId: 'assistant',
  threadId: 'main',
  session: {
    create: { metadata: { title: 'New chat' } },
  },
});

client.onAny((event) => applyEvent(event));

const state = await chat.subscribeLive();

const submission = await chat.submitMessage({ contents: [{ $type: 'text', text: 'Hello' }] }, {
  runConfig: {
    modelId: 'gpt-4o',
    chat: { temperature: 0.3 },
  },
});

console.log(`started run ${submission.runtimeRunId} at ${submission.startedAt}`);
```

## Client Tools

Use `client.tools` to register tool handlers. The client automatically responds to `CLIENT_TOOL_INVOKE_REQUEST`.

```typescript
client.tools.register('get_active_view', () => ({
  activeView: 'chat',
}));

client.tools.registerToolHarness(browserToolHarness, (request) => {
  return runBrowserTool(request.toolName, request.arguments);
});
```

Handlers may return:

- a string, converted to a text tool result
- a JSON value, converted to a JSON tool result
- `ToolResultContent[]`
- a full `ClientToolInvokeOutcome`

## Low-Level Runtime

Raw event APIs remain available for protocol-level behavior that does not belong in transcript state: permission dialogs, clarification UI, continuation controls, middleware/status UI, audio, debugging, custom telemetry, and other app-specific event handling.

```typescript
import { AgentClient, EventTypes } from '@hpd-research/hpd-agent-client';

const client = new AgentClient('http://localhost:5135');

client.on(EventTypes.TEXT_DELTA, (event) => {
  process.stdout.write(event.text);
});

const state = await client.getThreadState('assistant', 'session-1', 'main');
if (!state) throw new Error('Thread not found');

await client.start({
  agentId: 'assistant',
  sessionId: 'session-1',
  threadId: 'main',
  after: { generation: state.observedCursor.generation, sequenceNumber: 0 },
});

await client.run({
  type: EventTypes.USER_MESSAGES_INPUT,
  agentId: 'assistant',
  sessionId: 'session-1',
  threadId: 'main',
  messages: [{
    role: 'user',
    contents: [{ $type: 'text', text: 'Hello' }],
  }],
});
```

## HTTP API

HTTP resource APIs are exposed through `client.api` and mirrored on `AgentClient` for convenience.

```typescript
const sessions = await client.api.searchSessions({
  metadata: { projectId: 'p1' },
});

const events = await client.api.getThreadEvents(sessions[0].id, 'main');
```

## Lifecycle transport

The SDK uses the committed, resumable SSE protocol. WebSocket support was removed because it could not provide snapshot hydration, acknowledged cursors, replay, or authoritative submission results.

```typescript
new AgentClient({ baseUrl: 'http://localhost:5135' });
```
