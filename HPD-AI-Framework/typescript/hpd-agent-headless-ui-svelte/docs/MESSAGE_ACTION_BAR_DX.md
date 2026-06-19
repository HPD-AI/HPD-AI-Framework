# Message Action Bar DX

`MessageActionBar` renders copy/edit/retry controls for one message. It does not
perform edit itself. Retry can either emit an app callback or explicitly call a
provided `ThreadRevisionState`.

```svelte
<MessageActionBar
  {message}
  onCopy={({ text }) => copyLog = text}
  onEditRequest={({ message }) => openEditor(message)}
  onRetryRequest={({ message }) => revisions.forkAndRetryMessage(message.id)}
/>
```

`Message` uses this same component when `showActions` is true.

## Custom Rendering

```svelte
<MessageActionBar {message}>
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

## Visibility

Use `autohide`, `float`, and `hideWhenBusy` for message-level action polish:

```svelte
<MessageActionBar
  {message}
  autohide="not-last"
  float="single-branch"
  hideWhenBusy
/>
```

## Styling Hooks

- `data-hpd-message-action-bar`
- `data-hpd-message-action="copy"`
- `data-hpd-message-action="edit"`
- `data-hpd-message-action="retry"`
- `data-visible`
- `data-floating`
- `data-copied`
- `data-pending`
