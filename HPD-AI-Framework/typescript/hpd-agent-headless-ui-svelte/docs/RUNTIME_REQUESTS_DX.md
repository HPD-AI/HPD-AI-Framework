# Runtime Requests DX

`ThreadRuntimeRequests` and `RuntimeRequest` render pending HPD runtime
requests from one `ThreadState`.

They are first-class runtime request primitives, not tool-call add-ons. They do
not create dialogs, reconstruct protocol events, or decide how app-specific
request events should be answered.

## Basic Use

```svelte
<ThreadRuntimeRequests {thread} />
```

Static rendering for tests, docs, and stories:

```svelte
<ThreadRuntimeRequests requests={requests} />
```

Render one request:

```svelte
<RuntimeRequest {thread} item={request} />
```

## Primitive Family

```text
ThreadRuntimeRequests
  list of pending requests

RuntimeRequest
  one request shell/router

RuntimeRequestPermission
RuntimeRequestClarification
RuntimeRequestClientTool
RuntimeRequestCustom
  kind-specific default renderers
```

Use direct leaves when the app already knows the request kind:

```svelte
<RuntimeRequestPermission {thread} item={request} />
<RuntimeRequestClarification {thread} item={request} />
<RuntimeRequestClientTool {thread} item={request} />
<RuntimeRequestCustom {thread} item={request} />
```

## Request Kinds

The core model is generic:

```ts
request.id
request.kind
request.sourceName
request.requestEventType
request.expectedResponseEventType
request.responsePolicy
request.target
request.visibility
```

Known kinds:

- `permission`
- `clarification`
- `client-tool`
- `custom`

Custom requests remain visible as `kind: 'custom'`.

## Actions

The action object exposes:

```ts
actions.approve(choice)
actions.deny(reason)
actions.clarify(answer)
actions.respondToClientTool(response, options)
actions.respond(input)
```

Use `respond(input)` for custom response events.

## Customize A Request

Replace a kind-specific body without replacing the root wrapper:

```svelte
<RuntimeRequest {thread} item={request}>
  {#snippet permission({ item, actions, props })}
    <section {...props}>
      <strong>{item.sourceName}</strong>
      <button onclick={() => actions.deny('Not allowed')}>Deny</button>
      <button onclick={() => actions.approve('allow-once')}>Allow once</button>
    </section>
  {/snippet}

  {#snippet custom({ item, actions, props })}
    <section {...props}>
      <pre>{JSON.stringify(item.event, null, 2)}</pre>
      <button
        onclick={() => actions.respond({
          type: item.expectedResponseEventType ?? 'CUSTOM_RESPONSE',
          requestId: item.id,
          sourceName: item.sourceName
        })}
      >
        Respond
      </button>
    </section>
  {/snippet}
</RuntimeRequest>
```

Replace the whole root element with `child`:

```svelte
<RuntimeRequest {thread} item={request}>
  {#snippet child({ item, actions, props })}
    <article {...props}>
      <strong>{item.kind}</strong>
      <button onclick={() => actions.respond(customResponse)}>Respond</button>
    </article>
  {/snippet}
</RuntimeRequest>
```

## Callback Props

Use callback props for side effects. These are not Svelte component events.

```svelte
<RuntimeRequest
  {thread}
  item={request}
  onDeny={({ item, reason }) => audit(item.id, reason)}
  onRespond={({ input }) => console.log(input)}
/>
```

Available callbacks:

- `onApprove`
- `onDeny`
- `onClarify`
- `onClientToolRespond`
- `onRespond`

## Customize The List

```svelte
<ThreadRuntimeRequests {thread}>
  {#snippet request({ item, actions, props, index })}
    <article {...props} data-index={index}>
      <strong>{item.kind}</strong>
      <span>{item.requestEventType}</span>
    </article>
  {/snippet}

  {#snippet empty()}
    <p>No pending requests.</p>
  {/snippet}
</ThreadRuntimeRequests>
```

## Styling Hooks

Stable attributes:

- `data-hpd-thread-runtime-requests`
- `data-hpd-runtime-request`
- `data-hpd-runtime-request-kind`
- `data-request-id`
- `data-request-kind`
- `data-request-source`
- `data-request-event-type`
- `data-response-event-type`
- `data-response-policy`
- `data-visibility`
- `data-hpd-runtime-request-header`
- `data-hpd-runtime-request-body`
- `data-hpd-runtime-request-actions`
- `data-hpd-runtime-request-approve`
- `data-hpd-runtime-request-deny`
- `data-hpd-runtime-request-submit`

## Boundary

Automatic client tools still belong in `AgentClient.tools`. These components
are for visible or user-mediated runtime requests: permissions,
clarifications, client tools, and user-defined durable request/response events.
