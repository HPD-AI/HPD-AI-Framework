# Context Display DX

`ContextDisplay` renders projected token usage for the current thread or a
standalone usage object.

HPD usage starts in the backend:

```text
MessageTurnFinishedEvent.Usage
  -> hpd-agent-client UsageDetails
  -> headless-ui ThreadProjectionSnapshot.contextUsage
  -> Svelte ContextDisplay primitives
```

## Basic Usage

```svelte
<ContextDisplayRoot {thread} modelContextWindow={128000}>
  <ContextDisplayBar />
  <ContextDisplayBreakdown />
</ContextDisplayRoot>
```

`modelContextWindow` is app-supplied because model limits are application and
provider policy.

## Variants

```svelte
<ContextDisplayRoot {thread} modelContextWindow={128000}>
  <ContextDisplayRing />
  <ContextDisplayText />
  <ContextDisplayBreakdown />
</ContextDisplayRoot>
```

The package provides:

- `ContextDisplayBar`
- `ContextDisplayRing`
- `ContextDisplayText`
- `ContextDisplayBreakdown`

## Standalone Usage

```svelte
<ContextDisplayRoot
  usage={{
    inputTokenCount: 1200,
    outputTokenCount: 300,
    totalTokenCount: 1500
  }}
  modelContextWindow={128000}
>
  <ContextDisplayText />
</ContextDisplayRoot>
```

## Custom Rendering

```svelte
<ContextDisplayRoot {thread} modelContextWindow={128000}>
  <ContextDisplayBar>
    {#snippet children({ fillProps, model })}
      <span>{Math.round(model.percent ?? 0)}%</span>
      <div class="track">
        <div {...fillProps} class="fill"></div>
      </div>
    {/snippet}
  </ContextDisplayBar>
</ContextDisplayRoot>
```

Use `child` when the app wants full root or primitive DOM ownership.

## Styling Hooks

Context display primitives expose stable HPD-owned attributes:

```css
[data-hpd-context-display-root] {
}

[data-hpd-context-display-root][data-has-usage] {
}

[data-hpd-context-display-root][data-severity="warning"] {
}

[data-hpd-context-display-root][data-severity="critical"] {
}

[data-hpd-context-display-bar] {
}

[data-hpd-context-display-bar-fill] {
}

[data-hpd-context-display-ring] {
}

[data-hpd-context-display-ring-progress] {
}

[data-hpd-context-display-text] {
}

[data-hpd-context-display-text-percent] {
}

[data-hpd-context-display-breakdown] {
}

[data-hpd-context-display-breakdown-row][data-row-key] {
}
```

Use the primitive snippets when the app needs to replace the DOM structure, for
example to render a product-specific meter, compact badge, or provider-specific
breakdown.
