# Thread Revisions

`createThreadRevisionController()` implements message edit/retry as fork-and-resend
workflows. It does not mutate an existing thread.

The backend primitive is `forkThread(fromMessageId)`, which copies messages
through `fromMessageId`. When `fromMessageId` is `null`, the backend forks from
the root before any source messages. That lets revisions replace or retry the
first user message without mutating the original thread.

## Retry

Retry sends the same user message again on a new thread path.

```ts
const revisions = createThreadRevisionController({
  client,
  agentId,
  sessionId,
  threadId,
});

await revisions.forkAndRetryMessage(messageId);
```

If `messageId` is an assistant message, the controller finds the previous user
message and resends that text. The result keeps both identities:
`clickedMessageId` is the clicked assistant, while `inputMessageId` is the
user message that was resent.

The fork metadata is normalized by the controller after retry resolution. Apps
may provide extra fork metadata, but the controller writes `revisionKind`,
`clickedMessageId`, `inputMessageId`, and `forkBoundaryMessageId` itself so a
retry from the user message and a retry from its assistant answer describe the
same semantic retry target.

```ts
await revisions.forkAndRetryMessage(messageId, {
  fork: ({ inputMessageId }) => ({
    name: `Retry ${inputMessageId}`,
  }),
});
```

## Edit

Edit sends replacement text on a new thread path. Edit is intentionally limited
to user messages.

```ts
await revisions.forkAndEditMessage(messageId, 'Use a shorter answer.');
```

If `messageId` is an assistant, system, or tool message, the controller throws
`ThreadRevisionError` with `code: 'unsupported-message-role'`. Assistant-message
actions should use retry, not edit.

## Boundary

The controller forks at the message before the user input being resent. If there
is no previous message, it forks from root with `fromMessageId: null`.
