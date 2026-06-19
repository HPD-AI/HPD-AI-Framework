# MessageActionBar Proposal

`MessageActionBar` is the Svelte action surface for one projected HPD message.
It is the single action bar model for messages: copy state, retry pending state,
visibility state, and future-friendly interaction locking.

The primitive stays HPD-native. It can request edit/retry from the app, and it
can explicitly use a provided `ThreadRevisionState` for retry. It never looks up
a global runtime and never selects or hydrates threads by itself.

## Goals

- Render copy/edit/retry controls for one message.
- Keep edit as a request so apps can choose inline edit, modal edit, or another
  workflow.
- Support explicit HPD retry integration through `ThreadRevisionState`.
- Expose copied, pending, visible, floating, and interaction state.
- Support Svelte snippets for full rendering control.

## Non-Goals

- No global runtime lookup.
- No implicit thread switching.
- No fork hydration or route updates.
- No built-in menu system yet.
- No feedback, speak, or export actions yet.

## API

```svelte
<MessageActionBar
  {message}
  onEditRequest={({ message }) => openEditor(message)}
  onRetryRequest={({ message }) => revisions.forkAndRetryMessage(message.id)}
/>
```

Explicit retry integration:

```svelte
<MessageActionBar
  {message}
  {revisions}
  onRevisionCreated={({ revision }) => selectThread(revision.threadId)}
/>
```

Custom rendering:

```svelte
<MessageActionBar {message} {revisions}>
  {#snippet children({ actions, props, state })}
    <div {...props.root}>
      {#if state.canCopy}
        <button {...props.copy} onclick={actions.copy}>
          {state.copied ? 'Copied' : 'Copy'}
        </button>
      {/if}
      {#if state.canEdit}
        <button {...props.edit} onclick={actions.requestEdit}>Edit</button>
      {/if}
      {#if state.canRetry}
        <button {...props.retry} onclick={actions.retry}>Retry</button>
      {/if}
    </div>
  {/snippet}
</MessageActionBar>
```

## Data Attributes

- `data-hpd-message-action-bar`
- `data-hpd-message-action="copy"`
- `data-hpd-message-action="edit"`
- `data-hpd-message-action="retry"`
- `data-visible`
- `data-floating`
- `data-copied`
- `data-pending`
- `data-status`
