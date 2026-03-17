# HPD-Agent TypeScript Client SDK

A lightweight, transport-agnostic TypeScript client SDK for consuming HPD-Agent events.

## Features

- **Zero dependencies** - Pure TypeScript, works in browser and Node.js
- **Transport agnostic** - Supports SSE and WebSocket transports
- **Type safe** - Full TypeScript support with typed event handlers
- **Bidirectional** - Built-in support for permissions, clarifications, and continuations
- **Simple API** - Callbacks + async/await for interactive flows

## Installation

```bash
npm install @hpd/hpd-agent-client
```

## Quick Start

```typescript
import { AgentClient } from '@hpd/hpd-agent-client';

const client = new AgentClient('http://localhost:5135');

let response = '';

await client.stream('conversation-123', [{ content: 'Hello!' }], {
  onTextDelta: (text) => {
    response += text;
    process.stdout.write(text);
  },
  onComplete: () => {
    console.log('\n\nDone!');
  },
  onError: (message) => {
    console.error('Error:', message);
  },
});
```

## With Permission Handling

```typescript
await client.stream(conversationId, messages, {
  onTextDelta: (text) => {
    updateUI(text);
  },

  onToolCallStart: (callId, name) => {
    showToolIndicator(name);
  },

  onPermissionRequest: async (request) => {
    // Show dialog and wait for user
    const userChoice = await showPermissionDialog({
      functionName: request.functionName,
      description: request.description,
      arguments: request.arguments,
    });

    return {
      approved: userChoice.approved,
      choice: userChoice.remember ? 'allow_always' : 'ask',
    };
  },

  onComplete: () => {
    hideLoadingIndicator();
  },
});
```

## WebSocket Transport

```typescript
const client = new AgentClient({
  baseUrl: 'http://localhost:5135',
  transport: 'websocket',
});

await client.stream(conversationId, messages, {
  onTextDelta: (text) => console.log(text),
  onPermissionRequest: async (req) => ({ approved: true }),
});
```

## With AbortController

```typescript
const controller = new AbortController();

// Cancel after 30 seconds
setTimeout(() => controller.abort(), 30000);

try {
  await client.stream(conversationId, messages, handlers, {
    signal: controller.signal,
  });
} catch (e) {
  if (e.name === 'AbortError') {
    console.log('Stream cancelled');
  }
}
```

## Event Handlers

| Handler | Description |
|---------|-------------|
| `onTextDelta` | Called for each text chunk |
| `onTextMessageStart` | Called when a new message starts |
| `onTextMessageEnd` | Called when a message completes |
| `onToolCallStart` | Called when a tool invocation starts |
| `onToolCallArgs` | Called with tool arguments |
| `onToolCallEnd` | Called when tool execution completes |
| `onToolCallResult` | Called with tool result |
| `onReasoning` | Called for reasoning/thinking content |
| `onPermissionRequest` | Called when permission is required (async) |
| `onClarificationRequest` | Called when clarification is needed (async) |
| `onContinuationRequest` | Called when agent wants to continue (async) |
| `onTurnStart` | Called when an agent iteration starts |
| `onTurnEnd` | Called when an agent iteration ends |
| `onComplete` | Called when the message turn completes |
| `onError` | Called on error |
| `onProgress` | Called on middleware progress |
| `onEvent` | Called for every event (raw access) |

## API Reference

### AgentClient

```typescript
class AgentClient {
  constructor(config: AgentClientConfig | string);

  stream(
    conversationId: string,
    messages: Array<{ content: string; role?: string }>,
    handlers: EventHandlers,
    options?: StreamOptions
  ): Promise<void>;

  abort(): void;

  readonly streaming: boolean;
}
```

### Configuration

```typescript
interface AgentClientConfig {
  baseUrl: string;
  transport?: 'sse' | 'websocket';
  headers?: Record<string, string>;
}
```

## License

MIT
