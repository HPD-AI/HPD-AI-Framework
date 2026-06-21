# Thread Conversation DX

`ThreadConversation` is the default one-thread chat shell. It composes the
existing primitives around one `ThreadState`; it does not own sessions,
workspaces, branch policy, revisions, or protocol event reconstruction.

```svelte
<ThreadConversation {thread} />
```

Default composition:

```text
ThreadConversation
  ThreadStatus
  ThreadTimelineViewport
    ThreadTimeline runtime requests excluded
    ThreadRuntimeRequests composer panel
    ThreadTimelineViewportFooter
      ThreadScrollToBottom
      ThreadComposer
```

## Basic Use

```svelte
<script lang="ts">
  import {
    ThreadConversation,
    createThreadState,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';

  const thread = createThreadState({ client, agentId, sessionId, threadId });
</script>

<ThreadConversation {thread} />
```

## Runtime Request Placement

`ThreadConversation` renders pending runtime requests once. The default placement
is `composer-panel`, which keeps blocking requests near the composer and filters
runtime request items out of the default timeline.

```svelte
<ThreadConversation {thread} runtimeRequestPlacement="composer-panel" />
```

Use `timeline` when the conversation should render runtime requests in event
order inside `ThreadTimeline`:

```svelte
<ThreadConversation {thread} runtimeRequestPlacement="timeline" />
```

Use `none` when the app provides its own runtime request UI:

```svelte
<ThreadConversation {thread} runtimeRequestPlacement="none" />
```

Custom `timeline` snippets receive the full `snapshot`, so they can opt into
inline runtime requests explicitly. Custom `requests` snippets are only rendered
for `composer-panel` placement.

## Region Snippets

Every major region is replaceable without rebuilding the thread lifecycle:

```svelte
<ThreadConversation {thread}>
  {#snippet header({ thread, snapshot })}
    <ThreadStatus {thread} />
  {/snippet}

  {#snippet timeline({ thread, snapshot })}
    <ThreadTimeline {thread} timeline={snapshot.timeline} />
  {/snippet}

  {#snippet requests({ thread })}
    <ThreadRuntimeRequests {thread} />
  {/snippet}

  {#snippet composer({ thread })}
    <ThreadComposer {thread} placeholder="Ask HPD..." />
  {/snippet}
</ThreadConversation>
```

Replace the whole root:

```svelte
<ThreadConversation {thread}>
  {#snippet child({ thread, snapshot, props })}
    <main {...props}>
      <ThreadStatus {thread} />
      <ThreadTimeline {thread} timeline={snapshot.timeline} />
      <ThreadComposer {thread} />
    </main>
  {/snippet}
</ThreadConversation>
```

## Pass-Through Props

Use `viewportProps` and `composerProps` for common default-shell tuning:

```svelte
<ThreadConversation
  {thread}
  viewportProps={{ turnAnchor: 'top', scrollBehavior: 'smooth' }}
  composerProps={{ submitMode: 'mod-enter', minRows: 2 }}
/>
```

## Mental Model

`ThreadState` exposes:

- `timeline`
- `workGroups`
- `transcriptMessages`
- `activity`
- `activeTools`
- `pendingRuntimeRequests`
- `textSubmissionState`

`ThreadConversation` does not reconstruct those values. It renders them through
the smaller primitives.

## Boundary

Do not put these responsibilities into `ThreadConversation`:

- active thread/session selection
- a global workspace runtime
- branch/fork switching policy
- revision client creation
- protocol event reconstruction
- reasoning/tool/final-answer lifecycle inference
- automatic tool execution
- modal/dialog policy
- file or multimodal protocol submission

The headless projection owns lifecycle correctness. The conversation shell owns
only the default visual composition for one thread.

## Styling Hooks

`ThreadConversation` exposes a stable root attribute for styling the composed
one-thread shell:

```css
[data-hpd-thread-conversation] {
}
```

The default shell renders other primitives inside this root, so apps can scope
conversation-level themes to their child hooks:

```css
[data-hpd-thread-conversation] [data-hpd-thread-status] {
}

[data-hpd-thread-conversation] [data-hpd-thread-timeline-viewport] {
}

[data-hpd-thread-conversation] [data-hpd-thread-composer] {
}
```

Use region snippets when the app needs a different layout, such as a split
panel, sticky header, external composer, or custom request drawer.
