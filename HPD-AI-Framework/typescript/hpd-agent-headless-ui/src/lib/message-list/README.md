# MessageList

Container for displaying chat messages with auto-scrolling and accessibility support.

## Features

-   **Auto-scroll** - Automatically scrolls to bottom when new messages arrive
-   **Keyboard navigation** - Optional keyboard navigation support
-   **Accessibility** - Proper ARIA attributes for screen readers (role="log", aria-live)
-   **Headless** - Zero styling, complete design control via data attributes
-   **Reactive** - Auto-updates message count

## Usage

### Basic Example

```svelte
<script>
  import { MessageList, Message } from '@hpd/hpd-agent-headless-ui';

  let { agent } = $props();
  let { messages } = agent.state;
</script>

<MessageList {messages}>
  {#each messages as message (message.id)}
    <Message {message} />
  {/each}
</MessageList>
```

### With Custom Styling

```svelte
<MessageList {messages} class="h-96 overflow-y-auto">
  {#each messages as message (message.id)}
    <Message {message} />
  {/each}
</MessageList>

<style>
  :global([data-message-list]) {
    max-height: 600px;
    overflow-y: auto;
    padding: 1rem;
    background: #f5f5f5;
  }

  :global([data-message-list][data-message-count="0"]) {
    /* Empty state styling */
  }
</style>
```

### Disable Auto-scroll

```svelte
<MessageList {messages} autoScroll={false}>
  {#each messages as message (message.id)}
    <Message {message} />
  {/each}
</MessageList>
```

### Custom ARIA Label

```svelte
<MessageList {messages} aria-label="Chat conversation history">
  {#each messages as message (message.id)}
    <Message {message} />
  {/each}
</MessageList>
```

## Props

| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `messages` | `Message[]` | **Required** | Array of messages to display |
| `autoScroll` | `boolean` | `true` | Auto-scroll to bottom when new messages arrive |
| `keyboardNav` | `boolean` | `true` | Enable keyboard navigation (sets tabindex) |
| `aria-label` | `string` | `"Message history"` | Accessibility label for screen readers |
| `ref` | `HTMLDivElement \| null` | `null` | Bindable element reference |
| `id` | `string` | auto-generated | Unique ID for the list |

## Snippets

### `child` Snippet

Full control over the root element:

```svelte
<MessageList {messages}>
  {#snippet child({ props })}
    <section {...props} class="custom-message-list">
      {#each messages as message (message.id)}
        <Message {message} />
      {/each}
    </section>
  {/snippet}
</MessageList>
```

### `children` Snippet (Default)

Default snippet for content:

```svelte
<MessageList {messages}>
  {#each messages as message (message.id)}
    <Message {message} />
  {/each}
</MessageList>
```

## Data Attributes

Use these attributes for styling:

| Attribute | Type | Description |
|-----------|------|-------------|
| `data-message-list` | boolean | Present on root element |
| `data-message-count` | number | Number of messages in the list |

## Accessibility

The component implements the following ARIA attributes:

- `role="log"` - Indicates a live region for chat messages
- `aria-label` - Describes the message list
- `aria-live="polite"` - Screen readers announce new messages
- `aria-atomic="false"` - Only announce changes, not entire list
- `tabindex` - Set to `0` when `keyboardNav={true}`, `-1` otherwise

## Auto-scroll Behavior

The component automatically scrolls to the bottom when:

1. New messages are added to the `messages` array
2. `autoScroll` prop is `true` (default)
3. The component has rendered (uses `requestAnimationFrame`)

The scroll happens **after** DOM updates to ensure proper positioning.

## Examples

### Empty State

```svelte
<MessageList {messages}>
  {#if messages.length === 0}
    <div class="empty-state">
      No messages yet. Start a conversation!
    </div>
  {:else}
    {#each messages as message (message.id)}
      <Message {message} />
    {/each}
  {/if}
</MessageList>

<style>
  .empty-state {
    padding: 2rem;
    text-align: center;
    color: #666;
  }
</style>
```

### With Loading Indicator

```svelte
<script>
  let { agent } = $props();
  let { messages, streaming } = agent.state;
</script>

<MessageList {messages}>
  {#each messages as message (message.id)}
    <Message {message} />
  {/each}

  {#if streaming}
    <div class="loading">AI is thinking...</div>
  {/if}
</MessageList>
```

## Related Components

- [`Message`](../message/README.md) - Individual message component
- [`Input`](../input/README.md) - Message input component
- [`createAgent`](../agent/README.md) - Agent state manager
