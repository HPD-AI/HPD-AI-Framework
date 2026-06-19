# Svelte Adapter User DX

`@hpd-research/hpd-agent-headless-ui-svelte` is the Svelte 5 adapter for the
framework-neutral HPD Agent headless UI core.

The package is intentionally thin:

- `createThreadState()` wraps one core `createThreadController()`.
- `createSessionListState()` wraps one core session-list controller.
- Session-list primitives render metadata-aware session rows and actions without
  owning app concepts like workspaces.
- `ThreadState` exposes `timeline`, `workGroups`, `transcriptMessages`, and
  `activity`.
- `createThreadBranchNavigationState()` wraps fork-group and child thread navigation
  metadata.
- `createThreadRevisionState()` wraps edit/retry fork-and-resend workflows.
- `Message` renders one projected message leaf.
- `MarkdownText` renders accumulated assistant text as Svelte-native markdown.
- `DiffViewer` renders unified or split source diffs from patches, old/new
  file bodies, markdown code blocks, or tool-call results.
- `ToolCall` renders one projected tool-call envelope and supports
  tool-specific custom snippets plus app-owned inspect handoffs.
- `MessageEdit` manages inline user-message edit drafts and calls revision
  state on save.
- `ThreadTimeline` composes timeline items into messages, work groups, runtime
  requests, progress rows, and warnings.
- `ThreadWorkGroup` renders one grouped turn lifecycle shell.
- `ThreadWorkParts` renders reasoning, draft, tool, progress, hook, and warning
  parts inside one work group.
- `ThreadStatus` renders passive thread status, and `ThreadStatusMetrics`
  renders passive activity details.
- `ThreadComposer` submits text and owns interrupt for one `ThreadState`.
- `ContextDisplay` primitives render projected turn token usage relative to an
  app-supplied model context window.
- `ThreadRuntimeRequests` renders pending runtime requests for one
  `ThreadState`.
- `Suggestion` renders suggested prompts that populate composer draft state or
  send through one `ThreadState`.
- `ComposerTriggerRoot` and `ComposerTriggerPopover` render inline `@mention`
  and `/command` pickers that patch composer value, metadata, and run config.
- `DirectiveText` renders structured composer directives from message metadata
  as inline chips inside message text.
- `SelectionToolbarRoot` and `SelectionToolbarQuote` capture selected
  timeline/message text as structured quote state.
- `ThreadStatus` renders ambient thread status.
- `ThreadError` renders normalized controller, thread, run, work, and tool
  errors.

It does not own a global active thread, protocol mapping, workspace state, or
timeline lifecycle.

## Ownership Boundary

The adapter exposes the projected model; it does not make app-level UX choices.

Handled for you:

- live events and hydrated events fold into the same thread snapshot shape
- user input appears as transcript messages
- reasoning and tool activity stay inside `workGroups`
- final assistant text is promoted to `transcriptMessages`
- runtime requests remain typed and actionable
- edit/retry helpers create branch-producing revisions
- branch navigation exposes fork groups and child thread metadata

Left to your app:

- whether completed work groups are visible, collapsed, expanded, or hidden
- how reasoning/tool/progress parts are styled
- whether a tool name or tool harness gets a fully custom renderer
- where an inspectable tool opens: side panel, modal, editor tab, or route
- where branch controls appear
- whether edit/retry switches to the new thread immediately
- route/cache/session/workspace ownership

## Sessions

Use the session-list primitives when the app needs a list of durable session
containers:

```svelte
<script lang="ts">
  import {
    createSessionListState,
    SessionListItem,
    SessionListItems,
    SessionListRoot,
    SessionListSubtitle,
    SessionListTitle,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';

  const sessions = createSessionListState({
    client,
    search: {
      metadata: { 'hpdos.workspaceKey': workspaceKey },
    },
  });

  await sessions.load();
</script>

<SessionListRoot sessionList={sessions}>
  <SessionListItems>
    {#snippet item({ item, index })}
      <SessionListItem
        {item}
        {index}
        onSelect={(item) => selectSession(item.id)}
      >
        <SessionListTitle />
        <SessionListSubtitle />
      </SessionListItem>
    {/snippet}
  </SessionListItems>
</SessionListRoot>
```

Metadata is generic. Apps can build workspace/project behavior by choosing
metadata keys and search filters; the component only renders the session model.

If a provider emits reasoning and final answer text with the same message id,
you do not need to special-case it. The core projection keeps reasoning in the
work group and renders only final answer text as the assistant transcript
message.

## Create A Thread State

```svelte
<script lang="ts">
  import {
    createThreadState,
    ThreadComposer,
    ThreadStatusIndicator,
    ThreadStatusMetrics,
    ThreadStatus,
    ThreadTimeline,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';

  const thread = createThreadState({
    client,
    agentId: 'agent-1',
    sessionId: 'session-1',
    threadId: 'thread-1',
  });

  let current = $state(thread.getSnapshot());

  $effect(() => thread.subscribe((snapshot) => {
    current = snapshot;
  }));
</script>

<ThreadStatus {thread}>
  {#snippet children(status)}
    <ThreadStatusIndicator {status} />
    <ThreadStatusMetrics {status} />
  {/snippet}
</ThreadStatus>
<ThreadError {thread} />
<ThreadTimeline {thread} />
<ThreadComposer {thread} />
```

`transcriptMessages` are final transcript leaves. Rich agent work belongs in
`timeline` and `workGroups`; `ThreadTimeline` renders that full model while
still allowing snippets to replace any item renderer.

## Submit Messages

`ThreadComposer` calls:

```ts
thread.sendMessage({
  contents: [
    { $type: 'text', text: trimmedValue },
    ...readyContents,
  ],
}, { runConfig });
```

The core controller stamps agent/session/thread scope. The composer does not
construct protocol events.

Use `Suggestion` beside `ThreadComposer` when you want prompt chips:

```svelte
let draft = $state('');

<Suggestion prompt="Explain this code" bind:targetValue={draft} />
<ThreadComposer {thread} bind:value={draft} />
```

Set `mode="send"` when a suggestion should submit immediately:

```svelte
<Suggestion {thread} prompt="Summarize this thread" mode="send" />
```

Use `SelectionToolbarRoot` when selected message text should become quoted
composer context:

```svelte
let quote = $state<ThreadQuote | null>(null);

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

## Edit And Retry

Use `createThreadRevisionState()` beside the active `ThreadState`:

```ts
const revisions = createThreadRevisionState({
  client,
  agentId,
  sessionId,
  threadId,
  onRevisionCreated: ({ threadId }) => selectThread(threadId),
});
```

Edit and retry create a new thread branch. `Message` can render the buttons, but
the app decides how to switch to the returned `threadId`.

These actions are branch-producing. They are different from rollback/backtrack,
which would mutate the current thread history, and different from steering,
which would send input into an active run. Those should stay separate
primitives.

Use `MessageEdit` when you want inline replacement text before creating the
fork:

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
>
  {#snippet view({ actions, message })}
    <Message {message} showActions onEditRequest={actions.startEdit} />
  {/snippet}

  {#snippet edit({ actionProps, actions, props, textareaAttachment })}
    <textarea {...props.textarea} {@attach textareaAttachment}></textarea>
    <button {...actionProps.cancel} onclick={actions.cancel}>Cancel</button>
    <button {...actionProps.save} onclick={actions.save}>Fork with replacement</button>
  {/snippet}
</MessageEdit>
```

When you want the adapter to hydrate the fork for you:

```ts
const result = await revisions.forkAndRetryMessage(message.id);

const nextThread = await createThreadStateFromRevision({
  client,
  agentId,
  sessionId,
  revision: result,
  hydrate: 'start',
});
```

Use `hydrate: 'rehydrate'` to load without connecting, or `hydrate: 'none'` when
your router/cache will hydrate later.

Hydrating the fork only loads thread UI state. It does not imply that external
workspace state or other app side effects have been reverted.

## Navigate Branches

Use `createThreadBranchNavigationState()` beside the active thread when the UI
needs branch controls:

```ts
const navigation = createThreadBranchNavigationState({
  client,
  sessionId,
  threadId,
  onSelected: ({ threadId }) => selectThread(threadId),
});

await navigation.load();
```

The state exposes `graph`, `forkGroups`, `activePathChoices`,
`runtimeChildren`, and `activeLabels`. Render those however your surface needs:
inline pager, dropdown, sidebar tree, or post-revision switcher.

Use `ThreadBranchSwitcher` when you have a computed
`ThreadBranchChoiceControl`:

```svelte
<ThreadBranchSwitcher
  {control}
  onSelect={({ threadId }) => selectThread(threadId)}
/>
```

For compact or icon-only layouts, use the explicit leaves:

```svelte
<ThreadBranchSwitcherPrevious {control} onSelect={selectBranch} />
<ThreadBranchSwitcherNumber {control} />
/
<ThreadBranchSwitcherCount {control} />
<ThreadBranchSwitcherNext {control} onSelect={selectBranch} />
```

Navigation does not hydrate or replace the active `ThreadState` by itself. Use
the selected `threadId` to update your route/cache, then create or hydrate the
thread state explicitly.

## Runtime Requests

Use `ThreadRuntimeRequests` with the same `thread`:

```svelte
<ThreadRuntimeRequests {thread} />
```

Known request kinds receive typed actions such as `approve`, `deny`, `clarify`,
and `respondToClientTool`. Custom request kinds use the generic `respond`
method.

## Errors

Use `ThreadError` with the same `thread`:

```svelte
<ThreadError {thread} />
```

It renders nothing when the snapshot has no error. When an error exists, it
renders the latest normalized `ThreadErrorInfo` and can optionally show all
current errors with `showAll`.

Custom backend error events do not need custom Svelte components. If the event
implements the backend `IErrorEvent` contract, it reaches the client as
`isError: true` plus `errorMessage`, and the core projection can expose it
through the same thread error model.

## Styling Hooks

The components are headless and unstyled. Stable attributes are documented in
each primitive-specific DX file. Common shell hooks include:

```css
[data-hpd-thread-conversation] {
}

[data-hpd-thread-status] {
}

[data-hpd-thread-timeline-viewport] {
}

[data-hpd-thread-timeline] {
}

[data-hpd-message] {
}

[data-hpd-thread-work-group] {
}

[data-hpd-runtime-request] {
}

[data-hpd-thread-composer] {
}

[data-hpd-thread-error] {
}
```

Use `data-hpd-*` attributes for styling and snippets when you want full DOM
control.
