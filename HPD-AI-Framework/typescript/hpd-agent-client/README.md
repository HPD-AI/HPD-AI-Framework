# HPD-Agent TypeScript Client SDK

TypeScript SDK for building HPD-Agent chat/runtime applications.

## Features

- **Chat runtime** - Open chat sessions, load branch history, and send text turns.
- **Conversation state** - Reduce streamed events and stored branch messages into stable conversation items.
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

chat.conversation.onChange((changes) => {
  for (const change of changes) {
    if (change.type !== 'reset') renderConversationItem(change.item);
  }
});

await chat.loadHistory();
await chat.sendText('Hello', {
  runConfig: {
    modelId: 'gpt-4o',
    chat: { temperature: 0.3 },
  },
});
```

## Conversation State

`ConversationState` normalizes live runtime events and stored branch messages into one UI-neutral model.

```typescript
import { ConversationState } from '@hpd/hpd-agent-client';

const conversation = new ConversationState();

client.onAny((event) => conversation.applyEvent(event));

const messages = await client.getBranchMessages('session-1', 'main');
conversation.applyBranchMessages(messages);

console.log(conversation.items);
```

It handles:

- `TEXT_MESSAGE_START`, `TEXT_DELTA`, `TEXT_MESSAGE_END`
- `REASONING_MESSAGE_START`, `REASONING_DELTA`, `REASONING_MESSAGE_END`
- `TOOL_CALL_START`, `TOOL_CALL_ARGS`, `TOOL_CALL_RESULT`, `TOOL_CALL_END`
- `MESSAGE_TURN_ERROR`
- stored `BranchMessage[]`

`ConversationState` is a transcript reducer, not a full event bus replacement. It intentionally ignores protocol events that do not directly become transcript items, such as permission requests, clarification prompts, continuation approvals, middleware/status events, audio events, lifecycle events, observability events, and custom protocol events. Handle those with `client.on(...)` or `client.onAny(...)`.

```typescript
chat.conversation.onChange(renderTranscriptChanges);

client.on(EventTypes.PERMISSION_REQUEST, showPermissionDialog);
client.on(EventTypes.CLARIFICATION_REQUEST, askClarifyingQuestion);
client.onAny(logProtocolEvent);
```

## Client Tools

Use `client.tools` to register tool handlers. The client automatically responds to `CLIENT_TOOL_INVOKE_REQUEST`.

```typescript
client.tools.register('get_active_view', () => ({
  activeView: 'chat',
}));

client.tools.registerHarness(browserHarness, (request) => {
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

const messages = await client.api.getBranchMessages(sessions[0].id, 'main');
```

## Transports

Runtime transports support SSE and WebSocket:

```typescript
new AgentClient({ baseUrl: 'http://localhost:5135', transport: 'sse' });
new AgentClient({ baseUrl: 'http://localhost:5135', transport: 'websocket' });
```
