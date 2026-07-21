# ThreadStatus DX

`ThreadStatus` renders the passive ambient state of one `ThreadState`. It is
read-only: it does not interrupt, retry, reconnect, or choose a thread.

Use `ThreadStatus` for a compact status badge. Use `ThreadStatusMetrics` when
you need passive tool/request metrics or blocked submission reason. Use
`ThreadComposer` for send and interrupt controls.

## Basic Use

```svelte
<ThreadStatus {thread} />
```

Default rendering is label-only. Tool counts, request counts, and blocked
submission reasons belong to `ThreadStatusMetrics`.

## Custom Rendering

Use `children` when you want the default wrapper and custom contents.

```svelte
<ThreadStatus {thread}>
  {#snippet children(status)}
    <strong>{status.label}</strong>
  {/snippet}
</ThreadStatus>
```

Use `ThreadStatusMetrics` when you want passive details next to the label.

```svelte
<script lang="ts">
  import {
    ThreadStatus,
    ThreadStatusIndicator,
    ThreadStatusMetrics,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';
</script>

<ThreadStatus {thread}>
  {#snippet children(status)}
    <ThreadStatusIndicator {status} />
    <ThreadStatusMetrics {status} />
  {/snippet}
</ThreadStatus>
```

Use `child` when you want full DOM control.

```svelte
<ThreadStatus {thread}>
  {#snippet child(status)}
    <aside {...status.props}>
      <span>{status.label}</span>
    </aside>
  {/snippet}
</ThreadStatus>
```

## Status Model

The snippet receives:

- `state`
- `label`
- `busy`
- `connected`
- `loading`
- `activity`
- `activeTools`
- `pendingRuntimeRequests`
- `textSubmissionState`
- `error`
- `threadExecution`
- `snapshot`

`state` is one of:

- `loading`
- `error`
- `disconnected`
- `requesting`
- `working`
- `ready`

State priority follows that order. For example, if a thread is disconnected and
also has an old active execution in the projection, the display state is
`disconnected`.

## Composition

`ThreadStatus` fits at the top of the conversation primitive stack:

```svelte
<ThreadStatus {thread} />
<ThreadRuntimeRequests {thread} />
<ThreadComposer {thread} />
```

It reads the same snapshot as the other primitives. It does not own state beyond
the current subscription value.

For interruption and message submission, compose the composer nearby:

```svelte
<ThreadStatus {thread} />
<ThreadComposer {thread} />
```

## Boundary

Use `ThreadStatus` for display. Put product policy in the app:

- reconnect controls
- retry controls
- interrupt buttons
- toast notifications
- modal placement
- workspace/session navigation

## Styling Hooks

`ThreadStatus` exposes stable HPD-owned attributes:

```css
[data-hpd-thread-status] {
}

[data-hpd-thread-status][data-status-state="ready"] {
}

[data-hpd-thread-status][data-status-state="working"] {
}

[data-hpd-thread-status][data-status-state="requesting"] {
}

[data-hpd-thread-status][data-busy] {
}

[data-hpd-thread-status][data-loading] {
}

[data-hpd-thread-status][data-connected] {
}

[data-hpd-thread-status-indicator] {
}

[data-hpd-thread-status-label] {
}

[data-hpd-thread-status-metrics] {
}

[data-hpd-thread-status-tools] {
}

[data-hpd-thread-status-requests] {
}

[data-hpd-thread-status-blocked] {
}
```

Use `children` or `child` when the status should become a badge, header row,
activity strip, or product-specific ambient state display.
