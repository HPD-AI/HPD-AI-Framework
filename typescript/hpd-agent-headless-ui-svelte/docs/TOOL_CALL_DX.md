# ToolCall DX

`ToolCall` renders the standard HPD tool-call envelope.

Use it when you have a projected `ToolCall` from a message, work part, active
tool list, or custom inspector.

```svelte
<script lang="ts">
  import { ToolCall } from '@hpd-research/hpd-agent-headless-ui-svelte';
</script>

<ToolCall tool={part.tool} />
```

The component is intentionally generic. It renders:

- tool name
- status
- duration
- tool harness and call type
- arguments
- result text
- error text
- an accessible local disclosure shell

## Custom Tool Rendering

Many tools need their own UI. Use the `children` snippet when the generic body
is not enough.

```svelte
<ToolCall {tool}>
  {#snippet children({ actions, elementProps, state, tool })}
    <section {...elementProps.root} class="workspace-tool">
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

The snippet receives the whole projected tool call. Apps can key off:

- `tool.name`
- `tool.callType`
- `tool.toolharnessName`
- `tool.args`
- `tool.result`
- `tool.resultText`
- `tool.error`

That keeps the package HPD-native without pretending every tool has the same
visual contract.

## Disclosure

`ToolCall` owns only local detail disclosure for the projected tool envelope.
It does not decide whether the surrounding turn/work group is visible.

By default:

- pending and executing tools are expanded
- failed tools are expanded
- completed tools are collapsed

Control the state when the app wants to preserve expansion across rerenders or
coordinate it with another inspector.

```svelte
let expanded = $state(false);

<ToolCall
  {tool}
  bind:expanded
  onExpandedChange={(next, details) => {
    console.log(next, details.reason);
  }}
/>
```

The `children` snippet receives `actions.expand()`, `actions.collapse()`, and
`actions.toggle()` for custom triggers. Generated `elementProps.trigger` and
`elementProps.content` include `aria-expanded`, `aria-controls`, `id`, and
`aria-labelledby`.

## Inspecting Tool Results

Some tool calls are compact in the timeline but deserve a larger app-owned
surface: a side panel, modal, editor tab, or route. Use `inspectable` and
`onInspect` for that handoff.

```svelte
<ToolCall
  {tool}
  inspectable={tool.name === 'edit_file'}
  inspectLabel="Inspect"
  onInspect={({ tool }) => inspector.open(tool)}
/>
```

`ToolCall` does not open the inspector. It only renders the optional inspect
button and calls `onInspect` with the projected `tool`, current `state`,
trigger element, event, and reason.

Custom renderers receive the same affordance:

```svelte
<ToolCall {tool} inspectable onInspect={openToolInspector}>
  {#snippet children({ actions, elementProps, state, tool })}
    <section {...elementProps.root}>
      <header {...elementProps.header}>
        <button {...elementProps.trigger}>{state.label}</button>
        {#if state.inspectable}
          <button {...elementProps.inspect}>Open inspector</button>
        {/if}
      </header>
    </section>
  {/snippet}
</ToolCall>
```

This replaces the useful part of the archived artifact UX: inline compact
trigger, rich out-of-band preview. It deliberately does not add an artifact
provider, slot registry, global open state, or side-panel layout policy.

## Parent Components

`MessageParts` and `ThreadWorkParts` both delegate tool rendering to
`ToolCall`.

```text
Message
  MessageParts
    ToolCall

ThreadWorkGroup
  ThreadWorkParts
    ToolCall
```

So apps can start with the default chat surface, then replace only the tool
leaf when a specific tool deserves richer rendering.

## Custom Events

Some tools may also emit custom events. `ToolCall` does not try to interpret
every possible event stream. The durable shared contract is still the projected
tool envelope. If an app projects custom events into the tool result or into a
tool-specific side channel, the `children` snippet is the right place to render
that richer state.

## Styling Hooks

`ToolCall` exposes stable HPD-owned attributes:

```css
[data-hpd-tool-call] {
}

[data-hpd-tool-call][data-tool-status="pending"] {
}

[data-hpd-tool-call][data-tool-status="executing"] {
}

[data-hpd-tool-call][data-tool-status="complete"] {
}

[data-hpd-tool-call][data-tool-status="error"] {
}

[data-hpd-tool-call][data-tool-active] {
}

[data-hpd-tool-call][data-tool-error] {
}

[data-hpd-tool-call-header] {
}

[data-hpd-tool-call-trigger] {
}

[data-hpd-tool-call-inspect] {
}

[data-hpd-tool-call-content] {
}

[data-hpd-tool-call-name] {
}

[data-hpd-tool-call-status] {
}

[data-hpd-tool-call-duration] {
}

[data-hpd-tool-call-meta] {
}

[data-hpd-tool-call-error] {
}

[data-hpd-tool-call-args] {
}

[data-hpd-tool-call-result] {
}
```

Use the `children` snippet when a specific tool needs a custom renderer, richer
result visualization, or custom-event display.
