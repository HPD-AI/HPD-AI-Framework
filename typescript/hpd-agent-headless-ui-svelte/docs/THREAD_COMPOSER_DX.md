# ThreadComposer DX

`ThreadComposer` is the text input primitive for one `ThreadState`. It owns
input behavior and submission. You own the DOM, layout, and styling.

## Basic Use

```svelte
<ThreadComposer {thread} />
```

On submit, the component calls:

```ts
thread.sendMessage({
  contents: [
    { $type: 'text', text: trimmedValue },
    ...readyContents,
  ],
  additionalProperties,
}, { runConfig });
```

The thread controller stamps the agent/session/thread scope. The composer does
not construct protocol events.

Use the same `thread` with transcript rendering to build a conversation surface:

```svelte
{#each snapshot.transcriptMessages as message (message.id)}
  <Message {message} />
{/each}
<ThreadComposer {thread} />
```

`ThreadComposer` submits message contents plus optional message metadata. The
core projection updates `transcriptMessages`, `timeline`, and `workGroups` when
events arrive.

## Bind Value And Ref

```svelte
<script lang="ts">
  let value = $state('');
  let textareaRef = $state<HTMLTextAreaElement | null>(null);
</script>

<ThreadComposer {thread} bind:value bind:textareaRef />
```

Use `textareaRef` for focus, selection, or product-specific DOM work.

## Custom DOM

Use `child({ state, actions, props })` for full control. Spread generated props
and attach `props.inputAttachment` to the textarea.

```svelte
<ThreadComposer {thread} bind:value bind:textareaRef autosize="pretext">
  {#snippet child({ state, actions, props })}
    <form {...props.root}>
      <button type="button" onclick={openFilePicker}>Attach</button>
      <textarea {...props.input} {@attach props.inputAttachment} />
      <button {...props.submit}>Send</button>
    </form>
  {/snippet}
</ThreadComposer>
```

The snippet receives:

- `state.value`
- `state.empty`
- `state.focused`
- `state.submitting`
- `state.busy`
- `state.disabled`
- `state.canSubmit`
- `state.canInterrupt`
- `state.blockedReason`
- `state.attachmentCount`
- `state.readyAttachmentCount`
- `state.textSubmissionState`
- `actions.setValue(value)`
- `actions.clear()`
- `actions.submit()`
- `actions.interrupt()`
- `actions.focus()`
- `textareaRef`
- `props.root`
- `props.input`
- `props.inputAttachment`
- `props.submit`
- `props.interrupt`

`blockedReason` and `textSubmissionState.reason` are intentionally product-facing.
Current reasons are:

- `empty`
- `disabled`
- `error`
- `runtime-request`
- `busy`
- `not-sendable`

Use `runtime-request` for copy like "answer the pending request first". Use
`busy` for active work where interrupt or waiting is the honest action.

## Run Config

Pass `runConfig` directly. The composer forwards it without interpreting it.
This is turn/run execution configuration, not message metadata.

```svelte
<ThreadComposer
  {thread}
  runConfig={{
    modelId,
    providerKey,
    skipTools,
    contextOverrides: {
      workspaceId
    }
  }}
/>
```

## Message Metadata

Pass `additionalProperties` for app-specific metadata that should travel with
the user message.

```svelte
<ThreadComposer
  {thread}
  additionalProperties={{
    workspaceId,
    source: 'command-palette'
  }}
/>
```

Quote UX uses the same generic metadata path:

```svelte
<script lang="ts">
  import type { ThreadQuote } from '@hpd-agent/headless-ui-svelte';

  let quote = $state<ThreadQuote | null>(null);
</script>

<ThreadComposer {thread} bind:quote />
```

When submitted, the composer sends `quote` as
`additionalProperties.quote`. With `clear="on-submit"`, a successful submit
clears the draft, attachments, and quote state.

## Autosize

Built-in autosize uses Pretext:

```svelte
<ThreadComposer
  {thread}
  autosize="pretext"
  minRows={1}
  maxRows={8}
  pretext={{ font: '16px Inter', lineHeight: 22 }}
/>
```

There is no DOM `scrollHeight` mode. Options:

- `autosize="pretext"` uses Pretext with `whiteSpace: 'pre-wrap'`.
- `autosize={false}` leaves sizing fully to the user.
- a custom strategy can return a height in pixels.

## Keyboard

Use `submitMode` to choose keyboard submission:

- `submitMode="enter"` submits on Enter.
- `submitMode="mod-enter"` submits on Ctrl+Enter or Cmd+Enter.
- `submitMode="none"` disables keyboard submission.
- Shift+Enter keeps a newline.
- IME composition does not submit.

```svelte
<ThreadComposer {thread} submitMode="mod-enter" />
```

Use `clear` to choose whether a successful send clears the draft:

```svelte
<ThreadComposer {thread} clear="never" />
```

## Styling Hooks

Stable attributes:

- `data-hpd-thread-composer`
- `data-hpd-thread-composer-textarea`
- `data-hpd-thread-composer-submit`
- `data-hpd-thread-composer-interrupt`
- `data-autosize`
- `data-blocked-reason`
- `data-can-submit`
- `data-empty`
- `data-busy`
- `data-disabled`

## Attachments

Pass a `FileAttachmentState` to submit uploaded files with the message.

```svelte
<FileAttachment state={attachments} />
<ThreadComposer {thread} attachments={attachments} />
```

The composer reads `attachments.readyContents`, appends those contents to the
text content, blocks while uploads are in flight, and blocks when any upload has
failed.

## Boundary

`ThreadComposer` submits message contents and message metadata. File selection,
uploading, retry, removal, and dropzone behavior belong to `FileAttachment`.
Execution settings belong to `runConfig`.
