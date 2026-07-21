# ThreadError DX

`ThreadError` renders the normalized error state for one `ThreadState`.

It is intentionally small. The backend owns error truth through `IErrorEvent`,
the TypeScript client preserves `isError` and `errorMessage`, and the headless
core projects those into `ThreadErrorInfo`. The Svelte adapter only subscribes
to the current thread state and exposes DOM/snippet control.

## Basic Use

```svelte
<ThreadError {thread} />
```

When there is no error, the component renders nothing.

The root uses `role="alert"` and `aria-live="polite"` so errors are announced
without requiring the app to build ARIA wiring from scratch.

## Custom Rendering

Use `children` when you want the default wrapper and custom contents.

```svelte
<ThreadError {thread}>
  {#snippet children(model)}
    <strong>{model.error?.message}</strong>
  {/snippet}
</ThreadError>
```

Use `child` when you want full DOM control.

```svelte
<ThreadError {thread}>
  {#snippet child(error)}
    <aside {...error.props.root}>
      <p>{error.label}</p>
      <button {...error.props.clearButton} onclick={error.actions.clear}>
        Clear
      </button>
    </aside>
  {/snippet}
</ThreadError>
```

## Error Model

The snippet receives:

- `error`
- `errors`
- `hasError`
- `label`
- `snapshot`
- `actions.clear()`

`error` is the latest projected error. `errors` includes all normalized errors
that can be derived from the current snapshot:

- controller-level failures
- thread/run failures
- work-group failures
- tool-call failures

The component does not know every backend event type. If a custom backend event
implements `IErrorEvent`, it reaches the client as `isError: true` with
`errorMessage`, and the headless projection can surface it without adapter
changes.

This is why `ThreadError` is thread-scoped instead of message-scoped. Message
errors are only one possible failure source in HPD; the thread projection is the
place where controller, thread-execution, work, tool, and custom event failures meet.

## Showing All Errors

```svelte
<ThreadError {thread} showAll />
```

`showAll` keeps the default rendering but lists every normalized error when the
snapshot contains more than one.

## Clearing

The default clear button calls:

```ts
thread.clearError();
```

That clears the current controller/projection error state. It does not delete
durable thread events or pretend a failed run succeeded.

## Boundary

Use `ThreadError` for inline error display. Keep these app-owned:

- toasts
- retry policy
- reconnect policy
- route changes after failure
- provider-specific troubleshooting copy

## Styling Hooks

`ThreadError` exposes stable HPD-owned attributes:

```css
[data-hpd-thread-error] {
}

[data-hpd-thread-error][data-error-kind] {
}

[data-hpd-thread-error][data-recoverable] {
}

[data-hpd-thread-error-message] {
}

[data-hpd-thread-error-list] {
}

[data-hpd-thread-error-list-item] {
}

[data-hpd-thread-error-list-item][data-error-kind] {
}

[data-hpd-thread-error-clear] {
}
```

Use `children` or `child` when an app needs provider-specific error copy,
action clusters, or a completely custom alert surface.
