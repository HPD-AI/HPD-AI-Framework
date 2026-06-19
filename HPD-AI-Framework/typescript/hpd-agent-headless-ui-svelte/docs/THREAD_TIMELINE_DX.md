# Thread Timeline DX

`ThreadTimeline` is the first Svelte component that renders the new
timeline-first contract. It replaces the old idea of a primary message-list
component.

Use it when you want to render the conversation as the projection understands
it: final transcript leaves, live or completed work groups, runtime requests,
progress rows, and warnings.

## Basic Usage

Pass a `ThreadState`.

```svelte
<script lang="ts">
  import { ThreadTimeline, createThreadState } from '@hpd-research/hpd-agent-headless-ui-svelte';

  const thread = createThreadState({ client, agentId, sessionId, threadId });
</script>

<ThreadTimeline {thread} />
```

Or pass static timeline items.

```svelte
<ThreadTimeline timeline={snapshot.timeline} />
```

The component subscribes to `thread.timeline` when `thread` is provided. Static
`timeline` wins when both are provided.

## What It Renders

Default rendering maps timeline items to leaf components:

- message item -> `Message`
- work item -> `ThreadWorkGroup`
- runtime request item -> `RuntimeRequest`
- progress item -> simple progress row
- warning item -> simple warning row

This is intentionally modest. The value of the component is the typed
composition contract, not a fixed visual system.

## Custom Rendering

Replace any item renderer with snippets.

```svelte
<ThreadTimeline {thread}>
  {#snippet message({ message })}
    <Message {message} class="bubble" />
  {/snippet}

  {#snippet work({ work, props })}
    <details {...props} class="work">
      <summary>{work.label}</summary>
      <span>{work.parts.length} steps</span>
    </details>
  {/snippet}

  {#snippet runtimeRequest({ request, actions })}
    <section data-request={request.kind}>
      <button onclick={() => actions.approve()}>Allow</button>
    </section>
  {/snippet}
</ThreadTimeline>
```

Users can render one tool call at a time, group many tool calls into one row,
collapse completed turns, keep active work expanded, or ignore work entirely.

## ThreadWorkGroup

`ThreadWorkGroup` renders one grouped turn lifecycle shell. It owns the native
`<details>` root, summary, status, and error surface, then delegates structured
part rendering to `ThreadWorkParts`.

```svelte
<ThreadWorkGroup {work} />
```

Customize individual parts with `workPart`.

```svelte
<ThreadWorkGroup {work}>
  {#snippet workPart({ part, work })}
    <div data-type={part.type}>
      {work.label}: {part.type}
    </div>
  {/snippet}
</ThreadWorkGroup>
```

The snippet is named `workPart` because `part` is a native HTML attribute.

## ThreadWorkParts

Use `ThreadWorkParts` directly when you already own the surrounding shell but
still want the package's HPD-native work lifecycle rendering.

```svelte
<details open={work.openByDefault}>
  <summary>{work.label}</summary>
  <ThreadWorkParts {work} />
</details>
```

It renders the structured internals of one work item:

- reasoning
- assistant draft
- tool, delegated to `ToolCall`
- tool group
- progress
- hook
- warning

It also applies the final-draft visibility rule. By default, a completed
assistant draft that was promoted to the transcript is hidden inside the work
group so the same answer is not rendered twice. Pass `showFinalDraft` when you
want to inspect that draft in place.

```svelte
<ThreadWorkParts {work} showFinalDraft />
```

The ownership boundary is:

- `ThreadTimeline` routes ordered timeline items.
- `ThreadWorkGroup` renders one work item shell.
- `ThreadWorkParts` renders the structured parts inside that work item.

## Tool Calls

`ThreadWorkParts` delegates tool parts to `ToolCall` by default.

```svelte
<ThreadWorkParts {work}>
  {#snippet workPart({ part, props })}
    {#if part.type === 'tool'}
      <ToolCall tool={part.tool} {...props} />
    {/if}
  {/snippet}
</ThreadWorkParts>
```

Use this when a specific tool or tool harness needs a richer renderer while the
timeline and work group boundaries stay unchanged.

## Boundary

`ThreadTimeline` does not:

- fetch thread data;
- own a controller;
- reconstruct protocol messages;
- decide app-level collapse policy;
- create a global active thread;
- replace `ThreadComposer`, `ThreadStatus`, or runtime request actions.

It composes the current `ThreadStateSnapshot.timeline` into Svelte render hooks.

## Styling Hooks

`ThreadTimeline` and the default timeline item renderers expose stable
HPD-owned attributes:

```css
[data-hpd-thread-timeline] {
}

[data-hpd-message] {
}

[data-hpd-thread-work-group] {
}

[data-hpd-thread-work-summary] {
}

[data-hpd-thread-work-state] {
}

[data-hpd-thread-work-error] {
}

[data-hpd-thread-work-parts] {
}

[data-hpd-thread-work-part] {
}

[data-hpd-thread-work-part][data-work-part-type="tool"] {
}

[data-hpd-thread-timeline-progress] {
}

[data-hpd-thread-timeline-warning] {
}
```

Use the `message`, `work`, `runtimeRequest`, `progress`, and `warning` snippets
when an app needs a different row shell. Use `ThreadWorkParts` and `workPart`
when only the internals of one work group need custom rendering.
