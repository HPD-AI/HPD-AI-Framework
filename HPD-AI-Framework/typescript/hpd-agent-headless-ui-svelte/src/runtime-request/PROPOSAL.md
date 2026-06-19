# Runtime Request Proposal

`RuntimeRequest` renders one pending HPD runtime request. It is a generic
request shell/router, not a permission-dialog primitive and not a tool-call
approval add-on.

## Goals

- Render all `RuntimeRequest` kinds from the headless core.
- Keep `ThreadRuntimeRequests` as a thin list container.
- Give known request kinds typed default renderers.
- Keep custom request events visible instead of dropping them.
- Let applications own layout, validation, modal behavior, and styling.
- Use Svelte 5 snippets and callback props instead of slots or dispatchers.

## Files

```text
src/runtime-request/
  PROPOSAL.md
  index.ts
  props.ts
  runtime-request.svelte
  runtime-request-permission.svelte
  runtime-request-clarification.svelte
  runtime-request-client-tool.svelte
  runtime-request-custom.svelte
  types.ts
```

## Shape

```text
RuntimeRequest
  root shell, header, generated props, action helpers

RuntimeRequestPermission
RuntimeRequestClarification
RuntimeRequestClientTool
RuntimeRequestCustom
  default kind-specific bodies
```

`ThreadRuntimeRequests` remains outside this folder because it answers a
different question: which pending requests should render?

## API

Default:

```svelte
<RuntimeRequest {thread} item={request} />
```

Named kind snippets:

```svelte
<RuntimeRequest {thread} item={request}>
  {#snippet permission({ item, actions, actionProps, props })}
    ...
  {/snippet}

  {#snippet clarification({ item, actions, actionProps, props })}
    ...
  {/snippet}

  {#snippet clientTool({ item, actions, actionProps, props })}
    ...
  {/snippet}

  {#snippet custom({ item, actions, actionProps, props })}
    ...
  {/snippet}
</RuntimeRequest>
```

Direct leaves:

```svelte
<RuntimeRequestPermission {thread} item={request} />
<RuntimeRequestClarification {thread} item={request} />
<RuntimeRequestClientTool {thread} item={request} />
<RuntimeRequestCustom {thread} item={request} />
```

## Actions

- `approve(choice?)`
- `deny(reason?)`
- `clarify(answer)`
- `respondToClientTool(response, options?)`
- `respond(input)`

`respond(input)` is the path for custom response events.

## Callback Props

Side effects use callback props:

- `onApprove`
- `onDeny`
- `onClarify`
- `onClientToolRespond`
- `onRespond`

Do not use `createEventDispatcher`.

## Boundary

This component family does not own protocol reconstruction, request lifecycle
cleanup, modal systems, or lower runtime behavior. The core projection owns
pending request state, and the runtime emits lifecycle terminal events when a
request resolves, expires, or is cancelled.
