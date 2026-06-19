# ThreadStatus Proposal

`ThreadStatus` is the passive read-only status primitive for one `ThreadState`.

It exists so apps can display ambient thread state without turning the
conversation surface into a workspace runtime.

Default rendering is intentionally label-only. Metrics and actions belong to
`ThreadStatusMetrics` and `ThreadComposer`.

## Shape

```svelte
<ThreadStatus {thread} />
```

Passive metrics:

```svelte
<ThreadStatus {thread}>
  {#snippet children(status)}
    <ThreadStatusIndicator {status} />
    <ThreadStatusMetrics {status} />
  {/snippet}
</ThreadStatus>
```

Custom rendering:

```svelte
<ThreadStatus {thread}>
  {#snippet child(status)}
    <div {...status.props}>
      {status.label}
    </div>
  {/snippet}
</ThreadStatus>
```

## State Priority

The component derives one display state from the current thread snapshot:

1. `loading`
2. `error`
3. `disconnected`
4. `requesting`
5. `working`
6. `ready`

`working` delegates to the core `isThreadBusy` selector so the Svelte adapter
does not duplicate core lifecycle logic.

## Boundaries

- Read-only only.
- No interrupt, retry, or reconnect buttons.
- No active tool/request metric rendering by default.
- No blocked submission reason by default.
- No protocol event reconstruction.
- No modal or notification policy.
- No session/thread navigation.
- No global active thread runtime.

The component subscribes to `ThreadState`, creates a small display model, and
hands that model to default markup or snippets.
