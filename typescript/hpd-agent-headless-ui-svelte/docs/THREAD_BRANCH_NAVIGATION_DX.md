# Thread Branch Navigation DX

Branch navigation is the graph/read side of thread revisions. It does not
create forks, hydrate a live thread state, or decide what route/view becomes
active after selection.

Use `createThreadBranchNavigationState()` when a UI needs global fork groups,
active path choices, or runtime child metadata:

```ts
const branches = createThreadBranchNavigationState({
  client,
  sessionId,
  threadId,
  onSelected: ({ threadId }) => {
    selectThread(threadId);
  },
});

await branches.load();
```

The state wraps the core `createThreadBranchNavigator()` primitive and exposes
a Svelte-readable store.

## What It Exposes

```ts
const snapshot = branches.getSnapshot();

snapshot.graph;
snapshot.threads;
snapshot.forkGroups;
snapshot.activePathChoices;
snapshot.runtimeChildren;
snapshot.activeLabels;
snapshot.hasForkGroups;
snapshot.hasActivePathChoices;
snapshot.hasRuntimeChildren;
```

Actions:

- `load(threadId?)`: load the session graph and derive navigation metadata for
  a thread.
- `refresh()`: reload the current thread's graph metadata.
- `selectThread(threadId)`: select any known thread and call `onSelected` if
  the thread changed.
- `selectForkGroupMember(groupId, threadId)`: select a specific member of a
  fork group.
- `previousInGroup(groupId)`: move to the previous member in that fork group.
- `nextInGroup(groupId)`: move to the next member in that fork group.

`onSelected` receives the selected `threadId`, previous thread id, trigger,
optional `groupId`, and current snapshot.

## Visual Patterns

There is no one true branch control. The same fork-group state can render as a
compact message-row pager, a header picker, a sidebar branch list, a tree, or a
post-edit toast.

Fork groups are global session facts, but message-row controls should come from
active path choices. This matters when a selected branch forked early and later
reaches the same visible row number as a different fork group on another path:
same row number is not the same choice point.

An active path choice says: the selected path reaches this message boundary,
and this fork-group member represents the selected path there. The selected
thread may be the exact member or a descendant of that member:

```ts
choice.selectedMember;
choice.selectedThreadId;
choice.relationship; // 'exact-member' | 'descendant-of-member'
```

For timeline UIs, use `getThreadBranchChoiceControlsByTimelineItem(...)`. It
uses the current active path plus the rendered timeline to group controls by the
message rows where they should render, even when work groups, runtime requests,
or progress rows sit between transcript messages. This avoids treating a
transcript message index as a rendered row index.

```svelte
<script lang="ts">
  import {
    getThreadBranchChoiceControlsByTimelineItem,
  } from '@hpd-research/hpd-agent-headless-ui';
  import { Message, ThreadBranchSwitcher, ThreadTimeline } from '@hpd-research/hpd-agent-headless-ui-svelte';

  const controlsByTimelineItem = $derived.by(() => {
    return getThreadBranchChoiceControlsByTimelineItem($branches.navigation, timeline);
  });
</script>

<ThreadTimeline {timeline}>
  {#snippet message({ item, message })}
    {@const controls = controlsByTimelineItem.get(item.id) ?? []}
    <Message {message} />
    {#each controls as control (control.groupId)}
      <ThreadBranchSwitcher
        {control}
        onSelect={({ threadId }) => branches.selectForkGroupMember(control.groupId, threadId)}
      />
    {/each}
  {/snippet}
</ThreadTimeline>
```

The control separates lineage from rendering:

- `control.boundaryMessageId` is the durable fork boundary, meaning the last
  copied shared message before divergence.
- `control.selectedMember.choiceMessageId` is the preferred message row anchor
  for the selected path.
- `control.selectedMember.choiceMessageIndex` is member-local context for
  labels, sorting, and debugging. It is not enough by itself to place an inline
  control.
- `control.choiceMessageIndex` mirrors the selected member's visual index for
  display/debugging.
- `control.renderTimelineItemId` is where the inline UI should draw the
  switcher in the current timeline.

The group-level `choiceMessageIndex` is still useful for ordering and describing
the canonical choice point. It is not the row placement contract. Inline
placement comes from the selected member. Root fork groups use member
`choiceMessageIndex: 0`. Message-boundary fork groups use the selected member's
first divergent transcript row. This keeps edit/retry controls beside the user
prompt that changed, even though the fork itself copied history through the
previous assistant response.

The selector does not guess from boundary ids or from a global group row. It
uses `selectedMember.choiceMessageId`. If that exact row is not in the current
timeline, no inline control is returned for that group. Timeline placements are
`'root'`, `'choice-message'`, or `'unplaced'`.

Inline branch switchers are timeline controls. Do not place branch switchers
from transcript indexes.

```svelte
{@const active = $branches.activePathChoices[0]}

<button disabled={!active?.previous} onclick={() => active && branches.previousInGroup(active.group.id)}>
  Previous
</button>

<span>{active ? `${active.position.current} / ${active.position.total}` : ''}</span>

<button disabled={!active?.next} onclick={() => active && branches.nextInGroup(active.group.id)}>
  Next
</button>
```

For a list:

```svelte
{#each $branches.forkGroups as group}
  {#each group.members as member}
    <button onclick={() => branches.selectForkGroupMember(group.id, member.threadId)}>
      {member.isSource ? 'Source' : `Fork ${member.index + 1}`}
      {member.name}
    </button>
  {/each}
{/each}
```

Runtime children are separate from fork groups. Subagent/tool-owned threads can
be rendered as activity rows, a side panel, or an inspection drawer without
becoming branch choices:

```svelte
{#each $branches.runtimeChildren as child}
  <button onclick={() => branches.selectThread(child.threadId)}>
    {child.subAgentName ?? child.name ?? child.threadId}
  </button>
{/each}
```

## Boundary

Branch navigation is read/select state. It does not automatically create a new
`ThreadState`.

After edit or retry:

```ts
const result = await revisions.forkAndRetryMessage(message.id);

await branches.refresh();
await branches.selectThread(result.threadId);
```

If the app wants the selected thread hydrated, call
`createThreadStateFromRevision()` or create a new `createThreadState()` from the
selected thread id. The adapter does not switch routes, preserve scroll, swap
conversation stores, or own workspace/session state.

## Styling Hooks

`createThreadBranchNavigationState()` is state, not DOM. It has no styling
hooks.

Style the branch controls and views built from that state:

```css
[data-hpd-thread-branch-switcher] {
}

[data-hpd-thread-branch-switcher-action] {
}

[data-hpd-thread-branch-switcher-label] {
}

[data-hpd-thread-branch-switcher-number] {
}

[data-hpd-thread-branch-switcher-count] {
}

[data-hpd-thread-timeline] [data-hpd-message] {
}
```

For custom trees, side panels, or runtime-child drawers, the app owns the DOM
and should add its own product-level hooks beside the HPD state helpers.
