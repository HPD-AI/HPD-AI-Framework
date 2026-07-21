# Thread Error

`ThreadError` is the Svelte adapter primitive for displaying recoverable thread failures.

The error truth stays below the adapter:

- C# emits thread-execution, turn, middleware, and tool failure events.
- `@hpd-research/hpd-agent-client` transports those events.
- `@hpd-research/hpd-agent-headless-ui` projects the events into thread state and exposes `getThreadErrors`.
- `ThreadError` subscribes to `ThreadState` and renders the normalized error model.

## Why This Exists

`ThreadStatus` can say that a thread is in an error state, but applications often need a dedicated surface for the actual error message, a dismiss/recovery affordance, and custom layouts.

This component keeps that concern out of app code without introducing a new error store.

This is intentionally not a message-local error primitive. Some UI libraries
render errors from the current message context, but HPD errors can come from the
controller, a thread execution, a work group, a tool call, or another backend event that
implements the error contract. `ThreadError` therefore reads the normalized
thread projection instead of asking an individual message whether it failed.

## Public Shape

```svelte
<ThreadError {thread} />
```

For custom rendering:

```svelte
<ThreadError {thread}>
  {#snippet children({ error, errors, actions })}
    <section>
      <p>{error?.message}</p>
      <button onclick={actions.clear}>Dismiss</button>
    </section>
  {/snippet}
</ThreadError>
```

For complete element control:

```svelte
<ThreadError {thread}>
  {#snippet child({ error, props, actions })}
    <aside {...props.root}>
      <strong>{error?.kind}</strong>
      <p>{error?.message}</p>
      <button {...props.clearButton} onclick={actions.clear}>Clear</button>
    </aside>
  {/snippet}
</ThreadError>
```

## Boundary

This component does not parse event streams, inspect JSONL, or infer provider behavior. It renders the normalized thread error model already produced by the core.

Keep retry policy, reconnect policy, toasts, and provider-specific remediation
outside this primitive. The primitive owns inline display and the recoverable
clear action only.
