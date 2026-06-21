# SelectionToolbar DX

`SelectionToolbarRoot` and `SelectionToolbarQuote` turn selected message text
into structured quote state. The toolbar is Svelte/DOM infrastructure only: it
tracks browser selection inside a scoped root and exposes quote actions through
typed context.

Quotes are not appended into the composer draft. A quote is separate state:

```ts
type ThreadQuote = {
  text: string;
  messageId?: string;
  threadId?: string;
  source?: 'selection' | string;
};
```

That keeps the user's draft text separate from the context they are replying to.

## Basic Quote Flow

```svelte
<script lang="ts">
  import {
    ComposerQuote,
    ComposerQuoteDismiss,
    ComposerQuoteText,
    SelectionToolbarQuote,
    SelectionToolbarRoot,
    ThreadComposer,
    ThreadTimeline,
    type ThreadQuote,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';

  let quote = $state<ThreadQuote | null>(null);
</script>

<SelectionToolbarRoot bind:quote>
  {#snippet children({ props })}
    <ThreadTimeline {thread} />

    <div {...props.toolbar}>
      <SelectionToolbarQuote />
    </div>
  {/snippet}
</SelectionToolbarRoot>

<ComposerQuote bind:quote>
  <ComposerQuoteText />
  <ComposerQuoteDismiss />
</ComposerQuote>

<ThreadComposer {thread} bind:quote />
```

`SelectionToolbarQuote` stores `{ text, messageId, source: 'selection' }` when
the selection is inside an element with `data-message-id`. It still works
without a message id; the quote simply has text only.

## Custom Toolbar

```svelte
<SelectionToolbarRoot bind:quote placement="below">
  {#snippet children({ props, state, actions })}
    <ThreadTimeline {thread} />

    <div {...props.toolbar} class="selection-menu">
      <SelectionToolbarQuote>
        {#snippet children({ selection })}
          Quote {selection?.text.length ?? 0} chars
        {/snippet}
      </SelectionToolbarQuote>

      <button type="button" onclick={actions.close}>Close</button>
    </div>
  {/snippet}
</SelectionToolbarRoot>
```

## Custom Composer Preview

```svelte
<ComposerQuote bind:quote>
  {#snippet children({ quote, clear, props })}
    <aside {...props}>
      <strong>Replying to selection</strong>
      <p>{quote.text}</p>
      <button type="button" onclick={clear}>Remove</button>
    </aside>
  {/snippet}
</ComposerQuote>
```

## Message Quote Rendering

`ThreadComposer` sends the quote as `additionalProperties.quote` on the user
message. `MessageQuote` renders an explicit quote, message
`additionalProperties.quote`, or a quote-shaped content part if one is present
in a message.

```svelte
<MessageQuote {message} />

<MessageQuote quote={{ text: 'Selected text', messageId: 'msg-1' }}>
  {#snippet children({ quote, props })}
    <blockquote {...props}>“{quote.text}”</blockquote>
  {/snippet}
</MessageQuote>
```

## Behavior

- Selection must start and end inside `SelectionToolbarRoot`.
- `minLength` defaults to `1`.
- `placement` is `above` by default and may be `below`.
- `closeOnQuote` defaults to `true`.
- `clearSelectionOnQuote` defaults to `true`.
- The toolbar uses fixed positioning from `Range.getBoundingClientRect()`.
- Cross-message selections still open, but produce no `messageId`.

## Styling Hooks

Selection and quote primitives expose stable HPD-owned attributes:

```css
[data-hpd-selection-toolbar-root] {
}

[data-hpd-selection-toolbar][data-open] {
}

[data-hpd-selection-toolbar][data-placement="above"] {
}

[data-hpd-selection-toolbar][data-placement="below"] {
}

[data-hpd-selection-toolbar-quote] {
}

[data-hpd-selection-toolbar-quote][data-disabled] {
}

[data-hpd-composer-quote] {
}

[data-hpd-composer-quote-text] {
}

[data-hpd-composer-quote-dismiss] {
}

[data-hpd-message-quote] {
}
```

Use snippets for full DOM ownership when the toolbar should become a menu,
popover, source-aware quote card, or product-specific composer preview.
