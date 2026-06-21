# ThreadBranchSwitcher DX

`ThreadBranchSwitcher` renders branch navigation for one
`ThreadBranchChoiceControl`.

The control is computed by the framework-neutral headless core from the durable
HPD thread graph. The Svelte adapter does not infer branch position from
messages, and it does not mutate active thread state directly.

## Basic Use

```svelte
<ThreadBranchSwitcher
  {control}
  onSelect={({ threadId }) => selectThread(threadId)}
/>
```

The default rendering includes:

- previous branch button
- current label
- next branch button

## Explicit Leaves

Use leaves when the app wants icon buttons, compact counters, or custom
placement:

```svelte
<ThreadBranchSwitcherPrevious {control} onSelect={selectBranch}>
  Back
</ThreadBranchSwitcherPrevious>
<ThreadBranchSwitcherNumber {control} />
/
<ThreadBranchSwitcherCount {control} />
<ThreadBranchSwitcherNext {control} onSelect={selectBranch}>
  Forward
</ThreadBranchSwitcherNext>
```

Available leaves:

- `ThreadBranchSwitcherPrevious`
- `ThreadBranchSwitcherNext`
- `ThreadBranchSwitcherLabel`
- `ThreadBranchSwitcherNumber`
- `ThreadBranchSwitcherCount`

The leaves take `control` explicitly. There is no hidden context provider.

## Selection

`onSelect` receives:

- `control`
- `direction`
- `member`
- `threadId`

The app usually passes `threadId` to its active-thread state or router.

## Boundary

The switcher is not a local message variant picker. HPD branches are real
threads, and the backend/client/headless core own the graph. The Svelte adapter
only renders the selected `ThreadBranchChoiceControl`.

## Styling Hooks

Branch switcher primitives expose stable HPD-owned attributes:

```css
[data-hpd-thread-branch-switcher] {
}

[data-hpd-thread-branch-switcher][data-group-id] {
}

[data-hpd-thread-branch-switcher][data-current] {
}

[data-hpd-thread-branch-switcher][data-total] {
}

[data-hpd-thread-branch-switcher-action] {
}

[data-hpd-thread-branch-switcher-action][data-direction="previous"] {
}

[data-hpd-thread-branch-switcher-action][data-direction="next"] {
}

[data-hpd-thread-branch-switcher-label] {
}

[data-hpd-thread-branch-switcher-number] {
}

[data-hpd-thread-branch-switcher-count] {
}
```

Use the explicit leaf components when the app needs icon buttons, compact row
controls, separate counters, or branch switchers positioned outside the default
root.
