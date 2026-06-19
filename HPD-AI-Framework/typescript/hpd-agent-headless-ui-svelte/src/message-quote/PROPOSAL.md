# MessageQuote Proposal

`MessageQuote` is the display primitive for quote metadata that is already
attached to a rendered message. It completes the quote UX loop started by
`SelectionToolbarRoot`, `SelectionToolbarQuote`, `ComposerQuote`, and
`ThreadComposer`.

## Boundary

`MessageQuote` is render-only.

It should:

- read quote metadata from an explicit `quote` prop or from a projected
  `message`
- render a blockquote by default
- expose generated DOM props to a `children` snippet
- keep stable data attributes for styling and tests

It should not:

- mutate composer state
- subscribe to thread state
- send messages
- inject quote text into model context
- own selection or floating toolbar behavior

## Current Shape

```svelte
<MessageQuote {message} />
<MessageParts {message} />
```

Custom render:

```svelte
<MessageQuote {message}>
  {#snippet children({ quote, props })}
    <aside {...props}>
      <strong>Replying to</strong>
      <p>{quote.text}</p>
    </aside>
  {/snippet}
</MessageQuote>
```

## Quote Resolution

Resolution is intentionally narrow and durable:

1. Explicit `quote` prop
2. `message.additionalProperties.quote`
3. Quote-shaped content
4. Content `additionalProperties.quote`

The main durable path is `message.additionalProperties.quote`, because
`ThreadComposer` sends selected quote state as message metadata.

## Model Context Policy

The Svelte adapter should not decide whether a quote becomes model-visible
context. It should persist and render structured metadata only.

If HPD wants quote text to influence model calls, that should be a backend or
agent middleware policy. That layer owns provider-specific formatting,
conversation compaction, model history projection, and any security decisions
around user-provided quoted text.

## No Leaf Family Yet

Do not add `MessageQuoteText`, `MessageQuoteSource`, or context primitives yet.
The `children` snippet already gives full control for custom markup. Add leaves
only when multiple real consumers need consistent partial composition.
