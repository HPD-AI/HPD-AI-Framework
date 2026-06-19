# Reasoning DX

`Reasoning` renders reasoning text consistently across transcript messages and
live work groups.

```svelte
<Reasoning text={message.reasoning} status="complete" />
<Reasoning text={part.text} status={part.status} />
```

`Message` and `ThreadWorkGroup` use it by default. Reach for the component
directly when custom timeline rendering still wants the package's reasoning
styling hooks.

## Custom Rendering

```svelte
<Reasoning text={reasoning} status="streaming" label="Thinking">
  {#snippet children({ props, text })}
    <aside {...props}>
      <p>{text}</p>
    </aside>
  {/snippet}
</Reasoning>
```

## Styling Hooks

- `data-hpd-reasoning`
- `data-status`
- `data-empty`
- `data-hpd-reasoning-label`
- `data-hpd-reasoning-text`

`Reasoning` is presentational. It does not expose protocol events or lower-level
projection details.
