# SelectionToolbar Proposal

`SelectionToolbarRoot` is the Svelte adapter primitive for selected-text
actions. It owns DOM selection tracking and exposes a typed context for action
components such as `SelectionToolbarQuote`.

The clean break from the first implementation is intentional: quoting selected
text should produce structured quote state, not mutate the composer draft.

## Shape

```text
selection-toolbar/
  selection-toolbar-root.svelte
  selection-toolbar-quote.svelte
  context.ts
  props.ts
  types.ts
```

The companion renderers live separately:

```text
composer-quote/
  composer-quote.svelte
  composer-quote-text.svelte
  composer-quote-dismiss.svelte

message-quote/
  message-quote.svelte
```

## Responsibilities

`SelectionToolbarRoot`:

- renders the selectable root
- installs browser selection listeners through `{@attach}`
- computes toolbar position
- sets typed context
- owns the current `ThreadQuote | null`

`SelectionToolbarQuote`:

- reads root context
- converts the current selection to `ThreadQuote`
- stores it through the root action

`ComposerQuote`:

- renders current quote preview
- provides nested text/dismiss context

`MessageQuote`:

- renders an explicit quote, or quote-shaped message content when present

## Non-Goals

- Do not send messages directly.
- Do not call the client.
- Do not flatten quote text into composer draft text.
- Do not invent quote-specific transport. Quotes ride through generic message
  `additionalProperties`.

## Why Structured Quote State

The user's draft and the quoted context are different pieces of state. Keeping
them separate opens up preview, dismissal, source message linking, and future
durable quote metadata without parsing markdown from user text. `ThreadComposer`
persists quote state as `additionalProperties.quote` when it sends the message.
