# Message DX

`Message` renders one projected message leaf. It does not subscribe to a thread,
own lifecycle state, reconstruct protocol events, or render the full timeline.
Default content rendering is delegated to `MessageParts`, which renders the
accumulated `message.content` as one text part and keeps structured non-text
content from `message.contents` as separate parts.

## Basic Use

```svelte
<Message {message} />
```

Render transcript leaves from a `ThreadState`:

```svelte
{#each snapshot.transcriptMessages as message (message.id)}
  <Message {message} />
{/each}
```

Use `ThreadTimeline` later for full work-group rendering. `Message` is only the
leaf renderer.

## Customize Content

```svelte
<Message {message}>
  {#snippet children({ message, parts, status })}
    <header>{message.role}</header>
    <MessageParts {message} />
    <small>{status}</small>
  {/snippet}
</Message>
```

## Message Parts

Use `MessageParts` directly when you want structured content control.

```svelte
<MessageParts {message}>
  {#snippet part({ part, props })}
    {#if part.type === 'text'}
      <p {...props}>{part.text}</p>
    {:else if part.type === 'reasoning'}
      <Reasoning text={part.text} status={part.status} />
    {:else if part.type === 'content'}
      <a {...props} href={part.content.$type === 'uri' ? part.content.uri : undefined}>
        {part.content.$type}
      </a>
    {:else if part.type === 'tool'}
      <div {...props}>
        <ToolCall tool={part.tool} />
      </div>
    {/if}
  {/snippet}
</MessageParts>
```

Current render parts:

- `thinking`
- `reasoning`
- `text`
- `content`
- `tool`
- `cursor`

`MessageParts` renders one accumulated text part from `message.content`.
Raw text deltas remain available on `message.contents`, but they are not visual
text parts because markdown and directive rendering need the whole string.
Reasoning and non-text content still come from structured content where present.

Assistant text is rendered through `MarkdownText` by default. User text is
rendered through `DirectiveText` so structured `@mention` and `/command`
metadata can appear as inline chips.

## Message Action Bar

`MessageActionBar` owns the action surface for one rendered message. `Message`
can render it for convenience with `showActions`. Copy is local and can be
handled by the component. Edit and retry are thread/application workflows, so
the action surface only emits request callbacks for them.

```svelte
<Message
  {message}
  showActions
  onCopy={({ text }) => console.log('copied', text)}
  onEditRequest={({ message }) => openEditor(message)}
  onRetryRequest={({ message }) => retryFrom(message)}
/>
```

Default actions render only when requested:

- Copy renders when the message has copy text.
- Edit renders when `onEditRequest` is provided and the message is a user
  message.
- Retry renders when `onRetryRequest` is provided and the message is a user or
  assistant message.

Use the revision helpers when wiring thread edit/retry:

```svelte
<Message
  {message}
  showActions
  onEditRequest={({ message }) => openEditDraft(message)}
  onRetryRequest={({ message }) => revisions.forkAndRetryMessage(message.id)}
/>
```

The default action renderer applies the role policy for you. Use
`canEditMessage()` and `canRetryMessage()` when you are rendering a fully custom
action surface.

Customize copied text:

```svelte
<Message
  {message}
  showActions
  copyText={(message) => `${message.role}: ${message.content}`}
/>
```

Replace the action area:

```svelte
<Message {message} onCopy={({ text }) => copyLog = text}>
  {#snippet actionBar({ message, actions, props, state })}
    <div {...props.root}>
      {#if state.canCopy}
        <button {...props.copy} onclick={actions.copy}>
          Copy {message.role}
        </button>
      {/if}
      {#if state.canEdit}
        <button {...props.edit} onclick={actions.requestEdit}>Edit</button>
      {/if}
    </div>
  {/snippet}
</Message>
```

The `actionBar` snippet renders inside the default message wrapper. If you replace
the whole element with `child`, the snippet receives `actions` and `actionProps`
so you can place the action surface wherever your markup needs it. Use
`MessageActionBar` directly when actions should render outside the message.

## Reasoning

`Message` delegates `message.reasoning` to `Reasoning` by default.

```svelte
<Reasoning text={message.reasoning} status={message.streaming ? 'streaming' : 'complete'} />
```

Use a custom `children` snippet when you want complete control over where
reasoning appears.

## Tool Calls

`MessageParts` delegates projected tool parts to `ToolCall` by default.

```svelte
<ToolCall tool={part.tool} />
```

Use the `ToolCall` `children` snippet for tool-specific rendering without
replacing the whole message:

```svelte
<ToolCall tool={part.tool}>
  {#snippet children({ elementProps, state, tool })}
    <section {...elementProps.root}>
      <button {...elementProps.trigger}>{state.label}</button>
      <div {...elementProps.content}>
        <pre>{tool.name === 'read_file' ? state.resultText : state.argsText}</pre>
      </div>
    </section>
  {/snippet}
</ToolCall>
```

## Replace The Element

```svelte
<Message {message}>
  {#snippet child({ props, message, parts, status })}
    <article {...props}>
      <header>{message.role} · {status}</header>
      <MessageParts {message} />
    </article>
  {/snippet}
</Message>
```

## Styling Hooks

- `data-hpd-message`
- `data-hpd-message-parts`
- `data-hpd-message-part`
- `data-part-type`
- `data-content-type`
- `data-message-id`
- `data-role`
- `data-status`
- `data-streaming`
- `data-thinking`
- `data-has-tools`
- `data-has-reasoning`
- `data-hpd-message-content`
- `data-hpd-message-reasoning`
- `data-hpd-reasoning`
- `data-hpd-tool-call`
- `data-hpd-message-action-bar`
- `data-hpd-message-action="copy"`
- `data-hpd-message-action="edit"`
- `data-hpd-message-action="retry"`
