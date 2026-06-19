# DirectiveText

`DirectiveText` renders structured composer directives inside message text as
inline chips.

The composer writes readable text and durable directive metadata:

```ts
additionalProperties: {
  directives: [{
    id: 'workspace',
    label: 'Workspace',
    text: '@Workspace',
    trigger: '@',
    type: 'tool'
  }]
}
```

The headless core splits message text into plain text and directive parts. The
Svelte adapter renders those parts with snippets.

This is intentionally not assistant-ui's encoded `:type[label]{name=id}` text
format. HPD owns the message metadata, so directive meaning stays structured
instead of hidden inside display text.

## Default

```svelte
<DirectiveText {message} text="Ask @Workspace to inspect auth" />
```

## Custom Directive Chip

```svelte
<DirectiveText {message} text={part.text}>
  {#snippet directive({ directive, props })}
    <a {...props} href={`/directives/${directive.id}`}>
      {directive.text}
    </a>
  {/snippet}
</DirectiveText>
```

## Message Integration

`MessageParts` uses `DirectiveText` for text parts by default. Apps only need to
render `DirectiveText` directly when they are building a custom message part
renderer.
