# ToolCall Proposal

`ToolCall` is the Svelte leaf renderer for one projected HPD `ToolCall`.

It renders the standard durable tool envelope and leaves tool-specific visuals
to snippets. It owns local detail disclosure for the one projected tool call,
but it does not own timeline/work-group visibility. It does not reconstruct
protocol events, subscribe to a thread, execute client tools, or interpret every
custom event a tool may emit.

## Why This Exists

Tool calls appear in multiple projected places:

- `Message.toolCalls`
- `MessageParts` tool parts
- `ThreadWorkParts` tool parts
- `ThreadStateSnapshot.activeTools`
- future tool inspectors or activity panels

Without a shared leaf, `MessageParts` and `ThreadWorkParts` duplicate tool
markup and every app has to replace larger components just to customize one
tool row. `ToolCall` makes tool rendering a first-class adapter primitive.

## Architecture Boundary

```text
hpd-agent-client
  owns tool lifecycle event types and client tool request/response contracts

hpd-agent-headless-ui
  projects events into ToolCall, activeTools, message.toolCalls, and work parts

hpd-agent-headless-ui-svelte
  renders the projected ToolCall envelope
```

The Svelte component should not know how to run tools. It should not maintain a
registry of every possible tool-specific UI. It should render the common HPD
tool envelope and expose enough data for app-specific renderers.

## Public API

Default rendering:

```svelte
<ToolCall {tool} />
```

Custom rendering:

```svelte
<ToolCall {tool}>
  {#snippet children({ actions, elementProps, state, tool })}
    <section {...elementProps.root}>
      <header {...elementProps.header}>
        <button {...elementProps.trigger}>{state.label}</button>
        <span>{state.statusLabel}</span>
      </header>

      <div {...elementProps.content}>
        {#if tool.name === 'read_file'}
          <FilePreview args={tool.args} result={tool.result} />
        {:else}
          <pre>{state.resultText}</pre>
        {/if}
      </div>
    </section>
  {/snippet}
</ToolCall>
```

Controlled disclosure:

```svelte
<ToolCall
  {tool}
  bind:expanded
  onExpandedChange={(expanded, details) => {
    console.log(details.reason);
  }}
/>
```

App-owned inspection:

```svelte
<ToolCall
  {tool}
  inspectable={tool.name === 'edit_file'}
  inspectLabel="Inspect"
  onInspect={({ tool }) => inspector.open(tool)}
/>
```

`ToolCall` only exposes the affordance and callback. The app owns the actual
drawer, modal, editor tab, or route.

## Rendered Envelope

Default rendering includes:

- tool name
- status
- duration
- tool harness name
- call type
- arguments
- result text
- error text
- local detail disclosure
- optional inspect handoff button

Helpers expose:

- `createToolCallActions`
- `createToolCallState`
- `createToolCallElementProps`
- `getDefaultToolCallExpanded`
- `getToolCallStatusLabel`
- `formatToolCallDuration`
- `formatToolCallValue`
- `getToolCallVisibility`

## Custom Events

Some tools may emit custom events. `ToolCall` should not become a generic
custom-event renderer. The durable shared surface is the projected tool call.
Apps that project custom tool events into a richer side channel can render that
side channel through the `children` snippet while preserving the generated
props and stable data attributes.

## Artifact Boundary

The archived artifact component is treated as UX intent only. Its useful
behavior was a compact inline trigger that opened a richer out-of-band surface.
That maps to `ToolCall inspectable/onInspect` plus app-owned inspector UI.

Do not port:

- `Artifact.Provider`
- `Artifact.Root`
- `Artifact.Slot`
- `Artifact.Panel`
- teleport registries
- global one-open artifact state
- panel animation ownership

## Data Attributes

- `data-hpd-tool-call`
- `data-tool-id`
- `data-tool-name`
- `data-tool-status`
- `data-tool-active`
- `data-tool-error`
- `data-tool-harness`
- `data-tool-call-type`
- `data-hpd-tool-call-header`
- `data-hpd-tool-call-trigger`
- `data-hpd-tool-call-inspect`
- `data-hpd-tool-call-content`
- `data-hpd-tool-call-name`
- `data-hpd-tool-call-status`
- `data-hpd-tool-call-duration`
- `data-hpd-tool-call-meta`
- `data-hpd-tool-call-error`
- `data-hpd-tool-call-args`
- `data-hpd-tool-call-result`

## Non-Goals

- No tool execution.
- No client tool registry.
- No transport/event subscription.
- No app-level permission UX.
- No universal custom event rendering.
- No forced visual style for specific tool names.
- No timeline/work-group collapse policy.
- No artifact provider, slot registry, or app-level side-panel ownership.
