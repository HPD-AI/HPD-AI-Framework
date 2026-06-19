# Thread Branch Switcher

`ThreadBranchSwitcher` renders a branch choice control from HPD's durable thread
graph.

The lower layers own the truth:

- C# persists threads and fork groups.
- `@hpd-research/hpd-agent-client` exposes the thread graph.
- `@hpd-research/hpd-agent-headless-ui` computes active path choices and
  timeline placement.
- The Svelte adapter renders controls and reports the selected thread id.

## Why This Shape

Some chat UI libraries model branches as message-local variants. HPD does not.
A branch is a real thread in a session graph, so the switcher must preserve the
selected `threadId`, `groupId`, active-path relationship, and timeline placement.

The frontend primitive is still composable:

```svelte
<ThreadBranchSwitcher {control} onSelect={selectBranch} />
```

For custom layouts, use the explicit leaves:

```svelte
<ThreadBranchSwitcherPrevious {control} onSelect={selectBranch} />
<ThreadBranchSwitcherNumber {control} />
/
<ThreadBranchSwitcherCount {control} />
<ThreadBranchSwitcherNext {control} onSelect={selectBranch} />
```

## Boundary

`ThreadBranchSwitcher` does not load graphs or switch application state by
itself. The app handles `onSelect` and decides how to make the returned thread
active.

The component renders nothing when `control.position.total <= 1`.
