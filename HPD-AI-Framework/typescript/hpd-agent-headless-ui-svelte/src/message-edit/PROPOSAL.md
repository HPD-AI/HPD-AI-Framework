# MessageEdit Proposal

`MessageEdit` is the Svelte workflow wrapper for editing one user message by
creating a fork revision. It owns local edit draft state and delegates the
actual revision operation to `ThreadRevisionState`.

## Boundary

`MessageActionBar` can request editing. `MessageEdit` owns the draft UI and calls
`revisions.forkAndEditMessage(...)`. The application still chooses whether the
new fork becomes active, whether to hydrate it, and where branch navigation
appears.

## Public API

```svelte
<MessageEdit
  {message}
  {revisions}
  forkOptions={({ inputMessageId, sentText }) => ({
    name: `Edit ${inputMessageId}`,
    metadata: { replacementPreview: sentText.slice(0, 120) },
  })}
  onSaved={({ revision }) => selectThread(revision.threadId)}
/>
```

`forkOptions` is passed to the revision controller as its lower-level `fork`
option. The clearer prop name keeps the component API from sounding like an
imperative action.

## Snippet API

`MessageEdit` follows the adapter convention:

- `props` are structural element props;
- `actionProps` are button props;
- `actions` are callable behaviors.

```svelte
<MessageEdit {message} {revisions}>
  {#snippet view({ actions, message })}
    <Message {message} showActions onEditRequest={actions.startEdit} />
  {/snippet}

  {#snippet edit({ actionProps, actions, props, textareaAttachment, pending })}
    <textarea {...props.textarea} {@attach textareaAttachment}></textarea>
    <button {...actionProps.cancel} onclick={actions.cancel}>Cancel</button>
    <button {...actionProps.save} onclick={actions.save}>
      {pending ? 'Forking...' : 'Fork with replacement'}
    </button>
  {/snippet}
</MessageEdit>
```

## Data Attributes

- `data-hpd-message-edit`
- `data-editing`
- `data-pending`
- `data-empty`
- `data-can-save`
- `data-error`
- `data-hpd-message-edit-textarea`
- `data-hpd-message-edit-save`
- `data-hpd-message-edit-cancel`

## Non-Goals

- No branch switcher placement.
- No active-thread selection.
- No event copying or fork implementation.
- No retry workflow; retry belongs in `ThreadRevisionState` and can be
  requested by `MessageActionBar`.
