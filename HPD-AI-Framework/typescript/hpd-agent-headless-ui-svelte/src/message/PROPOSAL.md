# Message Proposal

`Message` is the Svelte root renderer for one projected core `Message` value.
`MessageParts` owns structured content rendering for the same message. Neither
component is responsible for transcript ordering, live work grouping, runtime
request lifecycle, or protocol event reconstruction.

## Architecture

```text
hpd-agent-client
  owns protocol helpers and durable message mapping

hpd-agent-headless-ui
  owns thread controller, timeline projection, navigator, and selectors

hpd-agent-headless-ui-svelte
  owns Svelte stores and Svelte components only
```

The component must not recreate `Workspace`, global active-thread state,
protocol event mapping, message reconstruction, runtime request resolver maps,
or thread timeline grouping.

## Svelte Rules

Use Svelte 5 primitives:

- `$props`
- `$derived`
- snippets
- callback props for actions
- `mount` and `unmount` in tests

Avoid legacy patterns:

- `export let`
- `$$props`
- `$$restProps`
- `<slot>`
- `createEventDispatcher`
- `on:click`
- `new Component(...)`
- `svelte/legacy`

## Component DX

Default rendering:

```svelte
<Message {message} />
```

Default rendering delegates content to `MessageParts`, which prefers
the accumulated `message.content` for visual text and keeps structured non-text
content from `message.contents` as separate parts.

```svelte
<MessageParts {message}>
  {#snippet part({ part, props })}
    {#if part.type === 'text'}
      <p {...props}>{part.text}</p>
    {:else if part.type === 'tool'}
      <ToolCallView tool={part.tool} />
    {/if}
  {/snippet}
</MessageParts>
```

`children` customizes content inside the default wrapper.

```svelte
<Message {message}>
  {#snippet children({ message, parts, status })}
    <strong>{message.role}</strong>
    <MessageParts {message} />
  {/snippet}
</Message>
```

`child` replaces the wrapper while receiving generated props.

```svelte
<Message {message}>
  {#snippet child({ props, message, parts, status })}
    <article {...props}>
      <MessageParts {message} />
    </article>
  {/snippet}
</Message>
```

Message action bar are a separate `MessageActionBar` component. `Message` can render
them by default with `showActions`, but apps can also place the action bar
wherever their layout needs it. Copy is local behavior. Edit and retry are
exposed only as request callbacks because they need thread/application policy.

```svelte
<Message
  {message}
  showActions
  onEditRequest={({ message }) => openEditor(message)}
  onRetryRequest={({ message }) => retryFrom(message)}
/>
```

Standalone usage:

```svelte
<MessageActionBar
  {message}
  onCopy={({ text }) => clipboardLog(text)}
  onEditRequest={({ message }) => openEditor(message)}
  onRetryRequest={({ message }) => retryFrom(message)}
/>
```

Custom action markup uses the same child props whether rendered through
`Message` or `MessageActionBar`:

```svelte
<Message {message}>
  {#snippet actionBar({ message, actions, props })}
    <div {...props.root}>
      <button {...props.copy} onclick={actions.copy}>
        Copy {message.role}
      </button>
    </div>
  {/snippet}
</Message>
```

Conversation-level rendering should be built from `ThreadStateSnapshot.timeline`
or `ThreadStateSnapshot.transcriptMessages`. `ThreadTimeline` groups work,
messages, tools, and requests. `Message` stays focused on one message leaf.

## Borrowed Ideas

From the archive:

- `Message`
- `child` and `children`
- snippet props
- stable data attributes
- minimal default rendering
- structured part rendering

From Bits UI:

- generated `props`
- headless parts
- native HTML prop passthrough
- escape hatches without forcing styling

Not borrowed:

- local `MessageState`
- boxed value machinery
- context-first architecture
- generic primitive framework internals
- Svelte 4 compatibility patterns
