# HPD-Agent TypeScript Client SDK

TypeScript SDK for building HPD-Agent chat/runtime applications.

## Features

- **Chat runtime** - Open chat sessions, load branch history, and send text turns.
- **Client tools** - Register browser/client-side tools with automatic response events.
- **Split API/runtime layers** - HTTP resources are handled by `AgentHttpApi`; SSE/WebSocket transports only stream runtime events.
- **Type safe protocol** - Typed agent events, session/branch DTOs, run config, client tools, and eval DTOs.
- **Zero runtime dependencies** - Pure TypeScript for browser and Node.js.

## Quick Start

```typescript
import { AgentClient } from '@hpd/hpd-agent-client';

const client = new AgentClient({ baseUrl: 'http://localhost:5135' });

client.tools.register('get_active_view', () => ({
  activeView: 'chat',
}));

const chat = await client.chat.open({
  agentId: 'assistant',
  branchId: 'main',
  session: {
    create: { metadata: { title: 'New chat' } },
  },
});

const history = await chat.getBranchEvents();
applyEvents(history);

client.onAny((event) => applyEvent(event));

await chat.subscribeLive();
await chat.submitText('Hello', {
  runConfig: {
    modelId: 'gpt-4o',
    chat: { temperature: 0.3 },
  },
});
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
- a full `ClientToolInvokeResponse`

## Low-Level Runtime

Raw event APIs remain available for protocol-level behavior that does not belong in transcript state: permission dialogs, clarification UI, continuation controls, middleware/status UI, audio, debugging, custom telemetry, and other app-specific event handling.

```typescript
import { AgentClient, EventTypes } from '@hpd/hpd-agent-client';

const client = new AgentClient('http://localhost:5135');

client.on(EventTypes.TEXT_DELTA, (event) => {
  process.stdout.write(event.text);
});

await client.start({
  agentId: 'assistant',
  sessionId: 'session-1',
  branchId: 'main',
});

await client.run({
  type: EventTypes.USER_TEXT_INPUT,
  agentId: 'assistant',
  sessionId: 'session-1',
  branchId: 'main',
  text: 'Hello',
});
```

## HTTP API

HTTP resource APIs are exposed through `client.api` and mirrored on `AgentClient` for convenience.

```typescript
const sessions = await client.api.searchSessions({
  metadata: { projectId: 'p1' },
});

const events = await client.api.getBranchEvents(sessions[0].id, 'main');
```

## Transports

Runtime transports support SSE and WebSocket. SSE separates live observation from input submission: `start()` opens the live event observer, while `run()` submits input to the runtime.

```typescript
new AgentClient({ baseUrl: 'http://localhost:5135', transport: 'sse' });
new AgentClient({ baseUrl: 'http://localhost:5135', transport: 'websocket' });
```
