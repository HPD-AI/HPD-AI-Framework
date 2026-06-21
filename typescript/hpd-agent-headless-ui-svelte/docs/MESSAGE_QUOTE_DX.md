# Message Quote DX

`MessageQuote` renders quote metadata on a sent message. It is the transcript
display piece for "replying to this selected text."

## Quote Flow

```text
SelectionToolbarRoot
  captures selected text

ComposerQuote
  previews pending quote

ThreadComposer
  sends additionalProperties.quote

MessageQuote
  renders quote metadata on the sent message
```

`MessageQuote` does not own the toolbar, composer preview, or model-context
policy.

## Basic Use

```svelte
<MessageQuote {message} />
<MessageParts {message} />
```

If `message.additionalProperties.quote` exists, the default renderer emits a
`blockquote`:

```text
> selected source text

User reply text
```

## Explicit Quote

Use an explicit quote when rendering a preview or a local model:

```svelte
<MessageQuote
  quote={{
    text: 'Selected source text',
    messageId: 'assistant-1',
    threadId: 'main',
    source: 'selection',
  }}
/>
```

Explicit `quote` wins over quote metadata on `message`.

## Custom Render

```svelte
<MessageQuote {message}>
  {#snippet children({ message, props, quote })}
    <aside {...props}>
      <strong>Replying to {quote.messageId ?? 'selection'}</strong>
      <p>{quote.text}</p>
    </aside>
  {/snippet}
</MessageQuote>
```

The snippet receives:

- `message`: the message being rendered, if provided
- `quote`: the resolved quote
- `props`: generated blockquote props and any rest props

## Quote Metadata

`ThreadComposer` sends quote state as message metadata:

```ts
additionalProperties: {
  quote: {
    text: 'selected text',
    messageId: 'source-message-id',
    threadId: 'source-thread-id',
    source: 'selection'
  }
}
```

This metadata is durable UI state. It is persisted through thread events and
projected back into messages.

## Model Context

`MessageQuote` does not inject quoted text into the model prompt. If HPD should
make quote text model-visible, do that in the backend or agent middleware as an
explicit policy. The UI should keep quote metadata structured.

## Styling Hooks

`MessageQuote` exposes stable HPD-owned attributes on the default blockquote:

```css
[data-hpd-message-quote] {
}

[data-hpd-message-quote][data-message-id] {
}
```

Use the `children` snippet when an app wants to render a quote card, source
link, avatar, or richer citation layout instead of the default blockquote.
