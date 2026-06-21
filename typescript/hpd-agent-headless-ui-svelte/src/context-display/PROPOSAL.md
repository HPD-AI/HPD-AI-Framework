# Context Display Proposal

`ContextDisplay` visualizes token usage relative to a model context window.

Unlike generic UI libraries, HPD already owns durable usage truth. The backend
emits `MessageTurnFinishedEvent.Usage`, the TypeScript client exposes that as
`UsageDetails`, and the framework-neutral headless core projects it into
`ThreadProjectionSnapshot.contextUsage`.

The Svelte adapter should only render that projected model.

## Goals

- Render context usage from a `ThreadState` or standalone usage object.
- Support bar, ring, text, and detailed breakdown primitives.
- Keep every DOM layer replaceable through snippets.
- Show input, output, cached input, reasoning, audio/text token details, and
  additional counts when present.
- Keep context-window knowledge app-supplied, not hardcoded in the primitive.

## Non-Goals

- Do not estimate tokens in the UI.
- Do not hardcode model context windows.
- Do not read raw protocol events in Svelte.
- Do not require a tooltip/popover dependency.
- Do not copy React provider architecture.

## API

From one `ThreadState`:

```svelte
<ContextDisplayRoot {thread} modelContextWindow={128000}>
  <ContextDisplayBar />
  <ContextDisplayBreakdown />
</ContextDisplayRoot>
```

Standalone:

```svelte
<ContextDisplayRoot {usage} modelContextWindow={128000}>
  <ContextDisplayRing />
  <ContextDisplayText />
</ContextDisplayRoot>
```

Custom DOM:

```svelte
<ContextDisplayBar>
  {#snippet children({ fillProps, model })}
    <span>{Math.round(model.percent ?? 0)}%</span>
    <div class="track">
      <div {...fillProps} class="fill"></div>
    </div>
  {/snippet}
</ContextDisplayBar>
```

## Boundary

The core projection stores usage. The Svelte adapter renders usage. The app
decides where the display belongs and what `modelContextWindow` applies.

