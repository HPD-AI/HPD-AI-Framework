# DirectiveText DX

`DirectiveText` renders structured composer directives in message text as
inline chips.

Use it when `ComposerTriggerDirective` stores semantic directive metadata on a
sent user message:

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

The visible message text stays readable:

```text
Ask @Workspace to inspect auth with /deep
```

`DirectiveText` uses the structured metadata to render `@Workspace` and `/deep`
as inline chips.

## Default Message Rendering

`MessageParts` uses `DirectiveText` for text parts by default:

```svelte
<MessageParts {message} />
```

## Direct Use

```svelte
<DirectiveText {message} text="Ask @Workspace to inspect auth" />
```

## Custom Chip

```svelte
<DirectiveText {message} text={part.text}>
  {#snippet directive({ directive, props })}
    <a {...props} href={`/tools/${directive.id}`}>
      {directive.text}
    </a>
  {/snippet}
</DirectiveText>
```

## Boundary

`DirectiveText` does not make directives model-visible. It renders metadata that
already exists on a projected message. App or backend policy decides whether
those directives become prompt context, run config, tool constraints, or simple
display metadata.

## Styling Hooks

`DirectiveText` exposes stable HPD-owned attributes for the root, plain text
parts, and directive chips:

```css
[data-hpd-directive-text] {
}

[data-hpd-directive-text-part] {
}

[data-hpd-directive-text-part][data-part-type="text"] {
}

[data-hpd-directive-text-chip] {
}

[data-hpd-directive-text-chip][data-directive-trigger="@"] {
}

[data-hpd-directive-text-chip][data-directive-trigger="/"] {
}

[data-hpd-directive-text-chip][data-directive-id] {
}

[data-hpd-directive-text-chip][data-directive-type] {
}
```

Use the `directive` snippet when a directive needs custom markup, routing, or an
interactive chip instead of the default inline span.
