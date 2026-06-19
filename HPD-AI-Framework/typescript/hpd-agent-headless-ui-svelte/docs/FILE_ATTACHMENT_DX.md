# FileAttachment DX

`FileAttachment` is the HPD-native file upload primitive for a `ThreadComposer`.
It uploads files to the agent content store first, then exposes ready
`AIContent` references to the composer.

## Basic Use

```svelte
<script lang="ts">
  import {
    FileAttachment,
    FileAttachmentState,
    ThreadComposer,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';

  const attachments = new FileAttachmentState({
    client,
    sessionId,
    threadId,
  });
</script>

<FileAttachment state={attachments} />
<ThreadComposer {thread} attachments={attachments} />
```

On submit, `ThreadComposer` sends text plus `attachments.readyContents`. It
blocks while files are uploading and stays blocked if any upload failed.

## Dropzone

Use the same `FileAttachmentState` for picker and drag/drop. The state is the
shared durable upload queue.

```svelte
<FileAttachmentDropzone state={attachments}>
  {#snippet children({ state })}
    <div class:dragging={state.dragging}>
      Drop files here
    </div>
  {/snippet}
</FileAttachmentDropzone>
```

## Custom Picker DOM

Use `child({ state, actions, props })` for complete DOM control. Spread the
generated input props and attach `props.inputAttachment`.

```svelte
<FileAttachment state={attachments}>
  {#snippet child({ state, actions, props })}
    <div {...props.root}>
      <input {...props.input} {@attach props.inputAttachment} />
      <button {...props.trigger}>Attach</button>

      {#each state.attachments as attachment}
        <button type="button" onclick={() => actions.remove(attachment.id)}>
          {attachment.file.name}
        </button>
      {/each}
    </div>
  {/snippet}
</FileAttachment>
```

The snippet receives:

- `state.attachments`
- `state.readyContents`
- `state.empty`
- `state.uploading`
- `state.errored`
- `state.ready`
- `state.canSubmit`
- `state.disabled`
- `actions.add(files)`
- `actions.remove(id)`
- `actions.retry(id)`
- `actions.clear()`
- `actions.open()`
- `props.root`
- `props.input`
- `props.inputAttachment`
- `props.trigger`

## Upload Boundary

`FileAttachmentState` accepts either:

- `client.uploadContent(sessionId, threadId, file, file.name)`
- a custom `upload({ sessionId, threadId, file })`

The primitive does not route files through a generic runtime adapter. HPD owns
the content store, so the adapter can upload directly and hand the composer
normal HPD `AIContent` references.

## Styling Hooks

Stable attributes:

- `data-hpd-file-attachment`
- `data-hpd-file-attachment-input`
- `data-hpd-file-attachment-trigger`
- `data-hpd-file-attachment-dropzone`
- `data-disabled`
- `data-empty`
- `data-uploading`
- `data-error`
- `data-ready`
- `data-dragging`

## Boundary

`FileAttachment` owns local upload UX and content references. It does not send
messages, create thread events, or manage thread runtime state. Use
`ThreadComposer` to submit the ready contents.
