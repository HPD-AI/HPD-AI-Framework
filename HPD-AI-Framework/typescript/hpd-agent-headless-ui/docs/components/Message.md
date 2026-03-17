# Message

A truly headless AI message component with built-in support for streaming, thinking states, tool execution, and reasoning.

---

## Overview

The Message component is a flexible, headless primitive for rendering AI chat messages. Unlike traditional message components, it's specifically designed for AI interactions with built-in state management for streaming text, thinking indicators, tool execution, and reasoning phases.

**Key characteristics:**
- **Truly Headless**: Ships zero CSS - you control 100% of the styling
- **AI-Aware**: Built-in support for streaming, thinking, tool execution, and reasoning
- **Reactive**: Fine-grained reactivity with runes
- **Accessible**: Automatic ARIA attributes for screen readers
- **Tiny**: < 2 KB gzipped

---

## Quick Start

```svelte
<script>
  import { Message } from '@hpd/hpd-agent-headless-ui';

  const message = {
    id: 'msg-1',
    role: 'assistant',
    content: 'Hello! How can I help you today?',
    streaming: false
  };
</script>

<Message {message}>
  {#snippet children({ content, role, streaming })}
    <div class="message {role}">
      {content}
      {#if streaming}<span class="cursor">▊</span>{/if}
    </div>
  {/snippet}
</Message>
```

---

## Installation

```bash
npm install @hpd/hpd-agent-headless-ui
```

---

## Key Features

- **🤖 AI-Specific States**: Built-in support for `streaming`, `thinking`, `executing`, and `complete` states
- **🎯 Status Derivation**: Automatically calculates message status based on AI activity
- ** Tool Execution Tracking**: Embedded tool call state management
- **💭 Reasoning Support**: Display AI thinking/reasoning process
- **♿ Accessibility**: Automatic ARIA live regions and busy states
- **📦 Tiny Bundle**: < 2 KB gzipped
- **🎨 Truly Headless**: Zero CSS shipped, data attributes only
- **⚡ Fine-Grained Reactivity**: Automatic updates with runes

---

## Basic Usage

### User Message

```svelte
<script>
  const userMessage = {
    id: 'msg-1',
    role: 'user',
    content: 'What is the weather like today?',
    streaming: false,
    timestamp: new Date()
  };
</script>

<Message message={userMessage}>
  {#snippet children({ content, role })}
    <div class="message user-message">
      <strong>You:</strong> {content}
    </div>
  {/snippet}
</Message>
```

### Assistant Message with Streaming

```svelte
<script>
  const assistantMessage = {
    id: 'msg-2',
    role: 'assistant',
    content: 'The weather is sun',
    streaming: true,
    thinking: false
  };
</script>

<Message message={assistantMessage}>
  {#snippet children({ content, streaming, status })}
    <div class="message assistant-message" data-status={status}>
      <strong>Assistant:</strong> {content}
      {#if streaming}
        <span class="cursor">▊</span>
      {/if}
    </div>
  {/snippet}
</Message>
```

### With Thinking State

```svelte
<script>
  const thinkingMessage = {
    id: 'msg-3',
    role: 'assistant',
    content: '',
    streaming: false,
    thinking: true
  };
</script>

<Message message={thinkingMessage}>
  {#snippet children({ thinking, status })}
    <div class="message" data-status={status}>
      {#if thinking}
        <div class="thinking-indicator">
          <span class="spinner"></span>
          Thinking...
        </div>
      {/if}
    </div>
  {/snippet}
</Message>
```

### With Reasoning

```svelte
<script>
  const reasoningMessage = {
    id: 'msg-4',
    role: 'assistant',
    content: 'Based on the data, I recommend TypeScript.',
    reasoning: 'Analyzing project requirements... Considering team expertise...',
    streaming: false,
    thinking: false
  };
</script>

<Message message={reasoningMessage}>
  {#snippet children({ content, reasoning, hasReasoning })}
    <div class="message">
      {#if hasReasoning}
        <div class="reasoning">
          <strong>Reasoning:</strong> {reasoning}
        </div>
      {/if}
      <div class="content">{content}</div>
    </div>
  {/snippet}
</Message>
```

### With Tool Execution

```svelte
<script>
  const messageWithTools = {
    id: 'msg-5',
    role: 'assistant',
    content: 'I found the weather information.',
    toolCalls: [
      {
        callId: 'tool-1',
        name: 'getWeather',
        messageId: 'msg-5',
        status: 'complete',
        args: { location: 'San Francisco' },
        result: '{"temp": 72, "condition": "Sunny"}',
        startTime: new Date(),
        endTime: new Date()
      }
    ]
  };
</script>

<Message message={messageWithTools}>
  {#snippet children({ content, toolCalls, hasActiveTools })}
    <div class="message">
      {#if toolCalls.length > 0}
        <div class="tools">
          {#each toolCalls as tool}
            <div class="tool" data-status={tool.status}>
               {tool.name}: {tool.status}
            </div>
          {/each}
        </div>
      {/if}
      <div class="content">{content}</div>
    </div>
  {/snippet}
</Message>
```

---

## Architecture

### Component Structure

The Message component follows a clear separation of concerns:

```
Message
├── message.svelte.ts (State Management)
│   └── MessageState class
│       ├── Reactive state ($state)
│       ├── Derived state ($derived)
│       └── HTML/ARIA props
├── message.svelte (Component)
│   └── Snippet-based rendering
└── types.ts (TypeScript Definitions)
```

### State Management

The `MessageState` class manages all reactive state:

```typescript
class MessageState {
  // Immutable props
  readonly id: string;
  readonly role: MessageRole;

  // Reactive state
  content = $state('');
  streaming = $state(false);
  thinking = $state(false);
  reasoning = $state('');
  toolCalls = $state<ToolCall[]>([]);

  // Derived state
  readonly status = $derived.by(() => {
    if (this.streaming) return 'streaming';
    if (this.thinking) return 'thinking';
    if (this.hasActiveTools) return 'executing';
    return 'complete';
  });
}
```

---

## API Reference

### Props

#### `message` (required)

The message object to render.

**Type:** `Message`

```typescript
interface Message {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  streaming?: boolean;
  thinking?: boolean;
  reasoning?: string;
  toolCalls?: ToolCall[];
  timestamp?: Date;
}
```

#### `child` (optional)

Snippet for full HTML control. Receives props object with data attributes.

**Type:** `Snippet<[{ props: MessageHTMLProps }]>`

```svelte
<Message {message} let:child>
  {@render child({ props: htmlProps })}
</Message>
```

#### `children` (optional)

Snippet for content customization. Receives all message state as props.

**Type:** `Snippet<[MessageSnippetProps]>`

```svelte
<Message {message}>
  {#snippet children({ content, role, streaming })}
    <div>{content}</div>
  {/snippet}
</Message>
```

#### `class` (optional)

Additional CSS classes to apply to the root element.

**Type:** `string`

---

### Snippet Props

When using the `children` snippet, you receive these props:

| Prop | Type | Description |
|------|------|-------------|
| `content` | `string` | Message text content |
| `role` | `'user' \| 'assistant' \| 'system'` | Message role |
| `streaming` | `boolean` | Whether the message is streaming |
| `thinking` | `boolean` | Whether AI is thinking |
| `hasReasoning` | `boolean` | Whether message has reasoning text |
| `reasoning` | `string` | Reasoning text (if any) |
| `toolCalls` | `ToolCall[]` | Tool calls embedded in message |
| `hasActiveTools` | `boolean` | Whether any tools are executing |
| `timestamp` | `Date` | Message timestamp |
| `status` | `MessageStatus` | Current message status |

### Message Status

The `status` prop is automatically derived from message state:

| Status | Description |
|--------|-------------|
| `'streaming'` | Message is currently streaming text |
| `'thinking'` | AI is thinking/reasoning (no text yet) |
| `'executing'` | Tools are being executed |
| `'complete'` | Message is complete, no active processes |

**Priority:** streaming > thinking > executing > complete

---

### Data Attributes

The Message component automatically generates these data attributes for CSS hooks:

| Attribute | Value | Description |
|-----------|-------|-------------|
| `data-message-id` | `string` | Message ID |
| `data-role` | `'user' \| 'assistant' \| 'system'` | Message role |
| `data-streaming` | `''` or `undefined` | Present when streaming |
| `data-thinking` | `''` or `undefined` | Present when thinking |
| `data-has-tools` | `''` or `undefined` | Present when has tool calls |
| `data-has-reasoning` | `''` or `undefined` | Present when has reasoning |
| `data-status` | `MessageStatus` | Current status |

**Usage:**

```css
.message[data-role="user"] {
  background: blue;
}

.message[data-streaming] {
  opacity: 0.8;
}

.message[data-status="thinking"] {
  background: purple;
}
```

---

### ARIA Attributes

Accessibility attributes are automatically applied:

| Attribute | Value | Description |
|-----------|-------|-------------|
| `aria-live` | `'polite' \| 'off'` | `'polite'` when streaming |
| `aria-busy` | `boolean` | `true` when streaming or thinking |
| `aria-label` | `string` | Message role label |

---

## Customization

### Full Styling Control

Since Message is truly headless, you have complete control over styling:

```svelte
<Message {message}>
  {#snippet children({ content, role, streaming, thinking, status })}
    <div
      class="message"
      class:user={role === 'user'}
      class:assistant={role === 'assistant'}
      class:streaming
      data-status={status}
    >
      <div class="header">
        <span class="role">{role}</span>
        <span class="status">{status}</span>
      </div>

      {#if thinking}
        <div class="thinking">Thinking...</div>
      {/if}

      <div class="content">
        {content}
        {#if streaming}
          <span class="cursor">▊</span>
        {/if}
      </div>
    </div>
  {/snippet}
</Message>

<style>
  .message {
    padding: 1rem;
    border-radius: 8px;
  }

  .message.user {
    background: #e3f2fd;
    align-self: flex-end;
  }

  .message.assistant {
    background: #f5f5f5;
    align-self: flex-start;
  }

  .message.streaming {
    opacity: 0.9;
  }

  .cursor {
    animation: blink 1s infinite;
  }

  @keyframes blink {
    0%, 50% { opacity: 1; }
    51%, 100% { opacity: 0; }
  }
</style>
```

### Using Data Attributes

```svelte
<Message {message}>
  {#snippet children({ content })}
    <div>{content}</div>
  {/snippet}
</Message>

<style>
  /* Style based on role */
  :global([data-role="user"]) {
    background: blue;
  }

  /* Style based on status */
  :global([data-status="streaming"]) {
    border-left: 3px solid #667eea;
  }

  :global([data-status="thinking"]) {
    background: #f0f0f0;
  }
</style>
```

---

## Advanced Usage

### Reusable Message Component

Create a reusable styled message component:

```svelte title="ChatMessage.svelte"
<script lang="ts">
  import { Message } from '@hpd/hpd-agent-headless-ui';
  import type { Message as MessageType } from '@hpd/hpd-agent-headless-ui';

  let { message }: { message: MessageType } = $props();
</script>

<Message {message}>
  {#snippet children({ content, role, streaming, thinking, status, reasoning, hasReasoning })}
    <div class="chat-message" data-role={role} data-status={status}>
      <div class="header">
        <span class="avatar">{role === 'user' ? '👤' : '🤖'}</span>
        <span class="role-label">{role}</span>
        <span class="status-badge">{status}</span>
      </div>

      {#if thinking}
        <div class="thinking-indicator">
          <div class="spinner"></div>
          <span>Thinking...</span>
        </div>
      {/if}

      {#if hasReasoning}
        <div class="reasoning-block">
          <strong>Reasoning:</strong>
          <p>{reasoning}</p>
        </div>
      {/if}

      <div class="message-content">
        {content}
        {#if streaming}
          <span class="typing-cursor">▊</span>
        {/if}
      </div>
    </div>
  {/snippet}
</Message>

<style>
  /* Your styles here */
</style>
```

**Usage:**

```svelte
<script>
  import ChatMessage from './ChatMessage.svelte';

  const message = {
    id: 'msg-1',
    role: 'assistant',
    content: 'Hello!',
    streaming: false
  };
</script>

<ChatMessage {message} />
```

### With Tool Call Visualization

```svelte
<Message {message}>
  {#snippet children({ content, toolCalls, hasActiveTools, status })}
    <div class="message" data-has-tools={hasActiveTools}>
      {#if toolCalls.length > 0}
        <div class="tool-calls">
          {#each toolCalls as tool}
            <div class="tool-call" data-status={tool.status}>
              <div class="tool-header">
                <span class="tool-icon">
                  {#if tool.status === 'executing'}🔄
                  {:else if tool.status === 'complete'} 
                  {:else if tool.status === 'error'} 
                  {:else}⏳{/if}
                </span>
                <span class="tool-name">{tool.name}</span>
                <span class="tool-status">{tool.status}</span>
              </div>

              {#if tool.args}
                <div class="tool-args">
                  <code>{JSON.stringify(tool.args, null, 2)}</code>
                </div>
              {/if}

              {#if tool.result}
                <div class="tool-result">
                  {tool.result}
                </div>
              {/if}
            </div>
          {/each}
        </div>
      {/if}

      <div class="content">{content}</div>
    </div>
  {/snippet}
</Message>
```

---

## Best Practices

### When to Use Message Component

  **Good use cases:**
- Displaying individual chat messages with AI-specific states
- Building chat interfaces with streaming support
- Showing tool execution in conversational UIs
- Implementing AI assistants with thinking/reasoning display

  **Not recommended for:**
- Static text display (just use `<p>` or `<div>`)
- Messages without AI-specific features
- Non-conversational interfaces

### Accessibility Considerations

1. **ARIA Live Regions**: The component automatically uses `aria-live="polite"` during streaming
2. **Busy States**: Sets `aria-busy="true"` when thinking or streaming
3. **Role Labels**: Provides `aria-label` with message role
4. **Screen Reader Announcements**: Streaming text is announced incrementally

**Additional recommendations:**
- Ensure sufficient color contrast for message roles
- Provide visual indicators beyond color (icons, badges)
- Test with screen readers (NVDA, JAWS, VoiceOver)

### Performance Tips

1. **Key Your Messages**: Always use unique `id` in the key attribute:
   ```svelte
   {#each messages as message (message.id)}
     <Message {message} />
   {/each}
   ```

2. **Limit Reactivity**: The component only re-renders when message props change
3. **Avoid Deep Nesting**: Keep snippet content relatively flat
4. **Tool Call Limits**: Consider pagination if toolCalls array is very large

---

## Examples

### ChatGPT-Style Interface

```svelte
<script>
  let messages = [
    { id: '1', role: 'user', content: 'Explain quantum computing', streaming: false },
    { id: '2', role: 'assistant', content: 'Quantum computing uses...', streaming: true }
  ];
</script>

<div class="chat-container">
  {#each messages as message (message.id)}
    <Message {message}>
      {#snippet children({ content, role, streaming })}
        <div class="message-wrapper" class:user={role === 'user'}>
          <div class="message-bubble">
            <div class="avatar">
              {role === 'user' ? '👤' : '🤖'}
            </div>
            <div class="text">
              {content}
              {#if streaming}<span class="cursor">▊</span>{/if}
            </div>
          </div>
        </div>
      {/snippet}
    </Message>
  {/each}
</div>
```

### iMessage-Style Interface

```svelte
<Message {message}>
  {#snippet children({ content, role, timestamp })}
    <div class="imessage-message" class:sent={role === 'user'} class:received={role === 'assistant'}>
      <div class="bubble">
        {content}
      </div>
      <div class="timestamp">
        {new Date(timestamp).toLocaleTimeString()}
      </div>
    </div>
  {/snippet}
</Message>

<style>
  .imessage-message {
    display: flex;
    flex-direction: column;
    margin: 0.5rem 0;
  }

  .sent {
    align-items: flex-end;
  }

  .sent .bubble {
    background: #007aff;
    color: white;
    border-radius: 18px 18px 4px 18px;
  }

  .received {
    align-items: flex-start;
  }

  .received .bubble {
    background: #e5e5ea;
    color: black;
    border-radius: 18px 18px 18px 4px;
  }

  .bubble {
    padding: 0.5rem 1rem;
    max-width: 70%;
  }

  .timestamp {
    font-size: 0.75rem;
    color: #8e8e93;
    margin-top: 0.25rem;
  }
</style>
```

---

## Library Design Philosophy

HPD Message follows a truly headless architecture:

| Feature | HPD Message |
|---------|-------------|
| **Truly Headless** |   Zero CSS |
| **Bundle Size** | < 2 KB |
| **AI States** | Full (streaming/thinking/executing) |
| **Reasoning** |   Built-in |
| **Tool Calls** |   Embedded |
| **Dependencies** |   Zero |

---

## TypeScript

Full TypeScript support with exported types:

```typescript
import type {
  Message,
  MessageProps,
  MessageHTMLProps,
  MessageSnippetProps,
  MessageStatus,
  MessageRole
} from '@hpd/hpd-agent-headless-ui';
```

---

## Related Components

- **MessageList**: Container for multiple messages with keyboard navigation
- **Input**: AI-aware message input with streaming awareness
- **ToolExecution**: Dedicated component for tool call visualization
- **PermissionDialog**: Handle AI permission requests
- **ClarificationDialog**: Handle AI clarification requests

---

## Troubleshooting

### Message not updating during streaming

Make sure you're updating the message object reference:

```svelte
//   Wrong - mutating
message.content += 'new text';

//   Correct - new reference
message = { ...message, content: message.content + 'new text' };
```

### Cursor not blinking

Ensure you're using the `streaming` prop in your snippet:

```svelte
{#snippet children({ content, streaming })}
  {content}
  {#if streaming}<span class="cursor">▊</span>{/if}
{/snippet}
```

### Status not updating

The status is derived automatically. Make sure the underlying states (`streaming`, `thinking`, `toolCalls`) are being updated.

---

## License

MIT
