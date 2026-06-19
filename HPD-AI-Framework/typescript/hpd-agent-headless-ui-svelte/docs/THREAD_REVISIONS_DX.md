# Thread Revisions DX

Thread revisions are fork-and-resend workflows. They do not mutate messages in
place.

This document uses "revision" narrowly: edit and retry create a new thread
branch, then resend user input on that branch. They are not rollback/backtrack,
and they are not active-turn steering.

Use `createThreadRevisionState()` beside `createThreadState()` when a UI wants
message edit or retry actions:

```ts
const revisions = createThreadRevisionState({
  client,
  agentId,
  sessionId,
  threadId,
  onRevisionCreated: ({ threadId }) => {
    selectThread(threadId);
  },
});
```

`ThreadState` represents one live thread. `ThreadRevisionState` creates the next
thread branch and returns its `threadId`; the application chooses how to switch
navigation.

## Primitive Boundaries

Keep these concepts separate:

- `fork/edit/retry`: creates a new thread branch. The existing thread remains
  unchanged.
- `hydrate/read`: loads the new thread into a `ThreadState`, optionally
  connecting to live events.
- `rollback/backtrack`: future same-thread history mutation. This should be a
  separate primitive because it changes the current thread instead of creating a
  branch.
- `steer`: future active-turn input. This should be a separate primitive guarded
  by the active run or turn id so stale steering cannot affect the wrong run.

The current revision layer only implements the first two pieces: fork-and-resend
plus optional hydration.

## Hydrate The Fork

Use `createThreadStateFromRevision()` when the app wants the adapter to create a
`ThreadState` for the fork.

```ts
const result = await revisions.forkAndRetryMessage(message.id);

const nextThread = await createThreadStateFromRevision({
  client,
  agentId,
  sessionId,
  revision: result,
  hydrate: 'start',
  hydrateOptions: { includeRuns: true },
});

selectThread(result.threadId, nextThread);
```

Hydration is explicit:

- `hydrate: 'start'` rehydrates and connects to live events. This is the
  default.
- `hydrate: 'rehydrate'` loads durable state without connecting.
- `hydrate: 'none'` creates the `ThreadState` without loading anything yet.

The helper does not switch routes, replace the current thread, preserve scroll,
or decide whether to show a comparison view. Those are application choices.

Hydration also does not imply that external app state, workspace state, or
side effects have been reverted. The headless adapter only creates and loads the
thread projection.

## Wire Message Action Bar

`Message` owns the action surface. Revision state owns the fork-and-resend
workflow.

```svelte
<script lang="ts">
  import {
    Message,
    canEditMessage,
    canRetryMessage,
    createThreadRevisionState,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';

  const revisions = createThreadRevisionState({
    client,
    agentId,
    sessionId,
    threadId,
    onRevisionCreated: ({ threadId }) => selectThread(threadId),
  });
</script>

<Message
  {message}
  showActions
  onEditRequest={canEditMessage(message)
    ? ({ message }) => openEditDraft(message)
    : undefined}
  onRetryRequest={canRetryMessage(message)
    ? ({ message }) => revisions.forkAndRetryMessage(message.id)
    : undefined}
/>
```

Edit is available for user messages. Retry is available for user and assistant
messages. Retrying an assistant message resends the previous user message on the
new fork.

## Edit Flow

Inline editing is an application interaction. Once the replacement text is
confirmed, call:

```ts
await revisions.forkAndEditMessage(message.id, draftText, {
  runConfig,
  fork: { name: 'Edited prompt' },
});
```

The state emits:

- `running`: a revision request is in progress.
- `activeKind`: `edit` or `retry`.
- `activeClickedMessageId`: the message that triggered the action.
- `lastRevision`: the latest successful fork result, including
  `clickedMessageId`, `inputMessageId`, `forkBoundaryMessageId`, `threadId`, and
  `sentText`.
- `error`: the latest failed revision error.

The state rejects a second edit or retry while `running` is true with
`ThreadRevisionStateError` and code `revision-in-progress`. Disable revision
buttons while running, or handle that error as a duplicate user action.

## Boundary

The current backend fork primitive copies messages through `fromMessageId`, so
the revision controller forks at the message before the user input being resent.
If there is no earlier boundary message, the core forks from root with
`fromMessageId: null`.

The returned revision lineage is action-level:

- `threadId`: the new forked thread.
- `clickedMessageId`: the message the user acted on.
- `inputMessageId`: the user message that was resent into the fork.
- `forkBoundaryMessageId`: the copied-through boundary message, or `null` for a
  root fork.
- `sentText`: the text sent into the new thread.

Thread-level ancestry such as `forkedFrom`, `forkedAtMessageId`, direct
ancestors, and child thread ids belongs to the client/backend `Thread` object
returned by the fork. Fork-group position is derived from the session thread
graph, not stored on the thread. Use both layers together: revision lineage
explains the UI action; thread ancestry explains the durable branch tree.

## Styling Hooks

`ThreadRevisionState` and `createThreadStateFromRevision()` do not render DOM.
They have no styling hooks.

Style the components that trigger or display revision workflows instead:

```css
[data-hpd-message-action-bar] {
}

[data-hpd-message-action="edit"] {
}

[data-hpd-message-action="retry"] {
}

[data-hpd-message-edit] {
}

[data-hpd-thread-branch-switcher] {
}
```

Use the related component snippets when edit/retry buttons, edit forms, or
branch switchers need product-specific structure.
