# ThreadComposer Proposal

`ThreadComposer` is the first input primitive for the Svelte adapter. It is a new Svelte 5-native component, not a port of the archived chat input.

## Goals

- Submit message contents through an existing `ThreadState`.
- Carry app metadata through message `additionalProperties`.
- Keep protocol event construction inside the headless controller/client stack.
- Give users direct control over the form and textarea DOM.
- Use Pretext for built-in autosize instead of hidden clone measurement.
- Stay Svelte 5-native with `$props`, `$bindable`, snippets, callback props, and attachments.

## Public Shape

```svelte
<ThreadComposer
  {thread}
  bind:value
  bind:quote
  bind:textareaRef
  autosize="pretext"
  minRows={1}
  maxRows={8}
  pretext={{
    font: '16px Inter',
    lineHeight: 22
  }}
>
  {#snippet child({ state, actions, props })}
    <form {...props.root}>
      <textarea {...props.input} {@attach props.inputAttachment} />
      <button {...props.submit}>Send</button>
    </form>
  {/snippet}
</ThreadComposer>
```

## Boundaries

- No Workspace.
- No global active thread runtime.
- No client protocol reconstruction.
- No message metadata in `runConfig`.
- No `scrollHeight` autosize mode.
- No hidden textarea clone.
- No Svelte 4 events or slots.
- No accessory component family in this first slice.
- No backwards-compatible legacy composer API.

## Autosize

Built-in autosize is Pretext-only:

```ts
prepare(value, font, {
  whiteSpace: 'pre-wrap',
  letterSpacing,
});

layout(prepared, width, lineHeight);
```

The result is clamped between `minRows` and `maxRows`. Empty text is treated as one visual row because Pretext correctly returns zero lines for an empty string while textareas still need a visible row.

Consumers can disable sizing with `autosize={false}` or provide a custom strategy.

## Message Metadata

`additionalProperties` is the message-level extension point. Quote UX uses this
path by sending structured quote state as `additionalProperties.quote`.

`runConfig` stays scoped to turn/run execution settings such as model/provider
options.
