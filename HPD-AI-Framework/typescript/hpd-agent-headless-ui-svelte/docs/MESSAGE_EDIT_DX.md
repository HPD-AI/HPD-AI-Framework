# Message Edit DX

`MessageEdit` wraps one user message and manages inline edit draft state. It
does not mutate the current thread. Saving calls
`revisions.forkAndEditMessage(message.id, draft)` and returns a fork revision.

Edit is a branch-producing action. It is not rollback/backtrack, and it is not
active-turn steering.

Use it beside `Message`:

```svelte
<MessageEdit
  {message}
  {revisions}
  runConfig={{ modelId }}
  forkOptions={({ inputMessageId, sentText }) => ({
    name: `Edit ${inputMessageId}`,
    metadata: {
      replacementPreview: sentText.slice(0, 120),
    },
  })}
  onSaved={({ revision }) => selectThread(revision.threadId)}
>
  {#snippet view({ actions, message })}
    <Message
      {message}
      showActions
      onEditRequest={actions.startEdit}
      onRetryRequest={({ message }) => revisions.forkAndRetryMessage(message.id)}
    />
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

## Responsibilities

`MessageEdit` owns:

- `editing`
- `draft`
- `pending`
- `error`
- `canSave`
- resetting the draft from `message.content` when edit starts
- Escape to cancel
- Enter to save, while Shift+Enter remains newline
- textarea autosize through the same pretext-friendly autosize path as
  `ThreadComposer`

The application owns:

- whether only one message can be edited at a time
- whether the new fork becomes active
- whether to hydrate the new fork
- where branch navigation appears
- whether any external workspace or app state should change
- the visual layout of the edit form

## Default Markup

Without snippets, the component renders a minimal view with an Edit button and a
minimal edit form. Production UIs should usually provide `view` and `edit`
snippets.

## Save Result

`onSaved` receives:

```ts
{
  message,
  revision,
  text,
}
```

`revision.clickedMessageId` is the edited message. `revision.inputMessageId`
is also that message for edit flows. The new fork is `revision.threadId`.

## Fork Metadata

`forkOptions` can be a static fork option or a callback. The callback receives
normalized revision details after the core has resolved the edit:
`{ kind, clickedMessageId, inputMessageId, forkBoundaryMessageId, sentText }`.
For edit, `clickedMessageId` and `inputMessageId` are the same user message.

```svelte
<MessageEdit
  {message}
  {revisions}
  forkOptions={({ inputMessageId, sentText }) => ({
    name: `Edit ${inputMessageId}`,
    metadata: {
      replacementPreview: sentText.slice(0, 120),
    },
  })}
/>
```

The controller adds canonical fork metadata automatically:
`revisionKind`, `clickedMessageId`, `inputMessageId`, and
`forkBoundaryMessageId`. App metadata should add app-specific fields, not
recompute those ids.

This keeps visual behavior in the application: after save, the app can switch to
`revision.threadId`, hydrate it with `createThreadStateFromRevision`, show a
branch chooser, or stay on the current thread.

## Styling Hooks

- `data-hpd-message-edit`
- `data-editing`
- `data-pending`
- `data-empty`
- `data-can-save`
- `data-error`
- `data-hpd-message-edit-textarea`
- `data-hpd-message-edit-save`
- `data-hpd-message-edit-cancel`
