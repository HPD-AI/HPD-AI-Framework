# Reasoning Proposal

`Reasoning` is the Svelte leaf renderer for reasoning text. It is intentionally
small: it renders text, status, stable data attributes, and a customization
snippet. It does not parse reasoning, reconstruct protocol events, or own work
group state.

## Why This Exists

Reasoning appears in two projected places:

- durable message reasoning on `Message`;
- streaming work-part reasoning inside `ThreadWorkGroup`.

Without a shared component, apps must customize both surfaces to get the same
reasoning treatment. `Reasoning` gives both defaults one adapter-level leaf.

## Public API

```svelte
<Reasoning text={message.reasoning} status="complete" />
<Reasoning text={part.text} status={part.status} />
```

Custom rendering:

```svelte
<Reasoning text={reasoningText} status="streaming">
  {#snippet children({ label, props, status, text })}
    <details {...props} open={status === 'streaming'}>
      <summary>{label}</summary>
      <p>{text}</p>
    </details>
  {/snippet}
</Reasoning>
```

## Data Attributes

- `data-hpd-reasoning`
- `data-status="complete"`
- `data-status="streaming"`
- `data-empty`
- `data-hpd-reasoning-label`
- `data-hpd-reasoning-text`

## Non-Goals

- No protocol semantics.
- No chain-of-thought naming.
- No event concatenation.
- No work grouping.
