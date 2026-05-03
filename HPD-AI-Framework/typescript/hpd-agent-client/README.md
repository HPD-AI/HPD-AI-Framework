# HPD-Agent TypeScript Client SDK

A lightweight TypeScript client SDK for HPD-Agent's event-native runtime.

## Features

- **Event-native API** - Send input events with `run(...)`, handle output events with `on(...)`
- **Transport agnostic** - Supports SSE, WebSocket, and MAUI transports
- **Type safe** - Typed event handlers keyed by `EventTypes`
- **Bidirectional** - Permissions, clarifications, continuations, interruptions, and client tools are all events
- **Realtime ready** - `start(...)` opens a continuous runtime for WebSocket-style apps
- **Zero runtime dependencies** - Pure TypeScript for browser and Node.js

## Installation

```bash
npm install @hpd/hpd-agent-client
```

## Quick Start

```typescript
import { AgentClient, EventTypes } from '@hpd/hpd-agent-client';

const client = new AgentClient('http://localhost:5135');

let response = '';

client.on(EventTypes.TEXT_DELTA, (event) => {
  response += event.text;
  process.stdout.write(event.text);
});

client.on(EventTypes.MESSAGE_TURN_FINISHED, () => {
  console.log('\n\nDone!');
});

client.onError((error) => {
  console.error('Error:', error.message);
});

await client.run({
  type: EventTypes.USER_TEXT_INPUT,
  text: 'Hello!',
  sessionId: 'conversation-123',
  branchId: 'main',
});
```

## With Permission Handling

```typescript
client.on(EventTypes.TEXT_DELTA, (event) => {
  updateUI(event.text);
});

client.on(EventTypes.TOOL_CALL_START, (event) => {
  showToolIndicator(event.name);
});

client.on(EventTypes.PERMISSION_REQUEST, async (event) => {
  const userChoice = await showPermissionDialog({
    functionName: event.functionName,
    description: event.description,
    arguments: event.arguments,
  });

  await client.run({
    type: EventTypes.PERMISSION_RESPONSE,
    permissionId: event.permissionId,
    sourceName: event.sourceName,
    approved: userChoice.approved,
    choice: userChoice.remember ? 'allow_always' : 'ask',
    reason: userChoice.approved ? undefined : 'User denied',
  });
});

await client.run({
  type: EventTypes.USER_TEXT_INPUT,
  text: 'Read this file and summarize it.',
  sessionId: conversationId,
  branchId: 'main',
});
```

## Run Configuration

`runConfig` travels on the input event.

```typescript
await client.run({
  type: EventTypes.USER_TEXT_INPUT,
  text: 'Give me a concise analysis.',
  sessionId: 'conversation-123',
  branchId: 'main',
  runConfig: {
    modelId: 'gpt-4o',
    chat: {
      temperature: 0.3,
      maxOutputTokens: 1200,
    },
  },
});
```

## WebSocket Runtime

For realtime apps, start the runtime once and send input events over time.

```typescript
const client = new AgentClient({
  baseUrl: 'http://localhost:5135',
  transport: 'websocket',
});

client.onAny((event) => {
  socketToBrowser.send(JSON.stringify(event));
});

await client.start({
  sessionId: 'conversation-123',
  branchId: 'main',
});

await client.run({
  type: EventTypes.USER_TEXT_INPUT,
  text: 'hello',
});

await client.run({
  type: EventTypes.INTERRUPTION_REQUEST,
  reason: 'User clicked stop',
  source: 'User',
});

await client.stop();
```

## Abort A Run

```typescript
const controller = new AbortController();

setTimeout(() => controller.abort(), 30_000);

await client.run(
  {
    type: EventTypes.USER_TEXT_INPUT,
    text: 'Do a deep analysis.',
    sessionId: 'conversation-123',
    branchId: 'main',
  },
  {
    signal: controller.signal,
  }
);
```

You can also stop the active transport directly:

```typescript
client.abort();
```

## Client Tools

Register a browser-side tool handler with `onClientToolInvoke`. The client automatically sends the `CLIENT_TOOL_INVOKE_RESPONSE` event.

```typescript
const client = new AgentClient({
  baseUrl: 'http://localhost:5135',
  onClientToolInvoke: async (request) => {
    const value = await runBrowserTool(request.toolName, request.arguments);

    return {
      requestId: request.requestId,
      success: true,
      content: [{ type: 'text', text: String(value) }],
    };
  },
});
```

## Event Handlers

```typescript
const subscription = client.on(EventTypes.TEXT_DELTA, (event) => {
  console.log(event.text);
});

const anySubscription = client.onAny((event) => {
  console.debug(event.type, event);
});

const errorSubscription = client.onError((error) => {
  console.error(error);
});

subscription.dispose();
anySubscription.dispose();
errorSubscription.dispose();
```

Handler ordering is deterministic for each output event:

1. Exact typed `on(EventTypes.X, ...)` handlers
2. `onAny(...)` handlers

## Common Input Events

| Event | Use |
|---|---|
| `USER_TEXT_INPUT` | Start a text turn |
| `USER_MESSAGES_INPUT` | Start a message-list turn |
| `PERMISSION_RESPONSE` | Respond to a permission request |
| `CLARIFICATION_RESPONSE` | Respond to a clarification request |
| `CONTINUATION_RESPONSE` | Respond to a continuation request |
| `CLIENT_TOOL_INVOKE_RESPONSE` | Send a client tool result |
| `INTERRUPTION_REQUEST` | Stop or interrupt active work |

## API Reference

```typescript
class AgentClient {
  constructor(config: AgentClientConfig | string);

  start(scope?: RuntimeScope): Promise<void>;
  stop(): Promise<void>;

  run(input: AgentRunInputEvent, options?: RunTransportOptions): Promise<void>;

  on<TType extends AgentEvent['type']>(
    type: TType,
    handler: (event: AgentEventOfType<TType>) => void | Promise<void>
  ): EventSubscription;

  onAny(handler: (event: AgentEvent) => void | Promise<void>): EventSubscription;
  onError(handler: (error: Error) => void | Promise<void>): EventSubscription;

  abort(): void;

  readonly streaming: boolean;
}
```

### Configuration

```typescript
interface AgentClientConfig {
  baseUrl: string;
  transport?: 'sse' | 'websocket' | 'maui';
  headers?: Record<string, string>;
  clientHarnesses?: clientHarnessDefinition[];
  onClientToolInvoke?: (
    request: ClientToolInvokeRequestEvent
  ) => Promise<ClientToolInvokeResponse>;
}
```

### Runtime Scope

```typescript
interface RuntimeScope {
  sessionId?: string;
  branchId?: string;
  agentId?: string;
  signal?: AbortSignal;
}
```

## Mental Model

```text
client.run(...)     sends input events to the agent
client.on(...)      handles specific output events from the agent
client.onAny(...)   handles all output events
client.start(...)   opens a continuous runtime connection
client.stop(...)    closes it
```

## License

MIT
