# DiffViewer DX

`DiffViewer` renders source-code changes from a unified patch or an old/new
file pair.

The framework-neutral core parses and shapes diff data. The Svelte adapter owns
the visual primitive, view mode, snippets, and styling hooks.

## Basic Use

```svelte
<DiffViewer patch={patchText} />
```

Generate a diff from two file bodies:

```svelte
<DiffViewer
  oldFile={{ name: 'agent.ts', content: oldText }}
  newFile={{ name: 'agent.ts', content: newText }}
/>
```

## View Modes

Unified view is the default:

```svelte
<DiffViewer patch={patchText} viewMode="unified" />
```

Split view renders old and new sides separately:

```svelte
<DiffViewer patch={patchText} viewMode="split" />
```

Use context folding and max lines for large patches:

```svelte
<DiffViewer patch={patchText} contextLines={2} maxLines={80} />
```

## Markdown Integration

Render `diff` fenced code blocks with `DiffViewer`:

```svelte
<MarkdownText text={assistantText}>
  {#snippet code({ lang, text })}
    {#if lang === 'diff' || lang === 'patch'}
      <DiffViewer patch={text} />
    {:else}
      <pre><code>{text}</code></pre>
    {/if}
  {/snippet}
</MarkdownText>
```

## ToolCall Integration

Tool calls can render patches using a tool-specific snippet:

```svelte
<ToolCall tool={tool}>
  {#snippet children({ elementProps, state, tool })}
    <section {...elementProps.root}>
      <button {...elementProps.trigger}>{state.label}</button>
      <div {...elementProps.content}>
        <DiffViewer patch={String(tool.result ?? '')} />
      </div>
    </section>
  {/snippet}
</ToolCall>
```

`DiffViewer` does not apply patches or call tools. It only renders already
available content.

## Custom Rendering

```svelte
<DiffViewer patch={patchText}>
  {#snippet header({ displayName, additions, deletions })}
    <header>
      <strong>{displayName}</strong>
      <span>+{additions} -{deletions}</span>
    </header>
  {/snippet}

  {#snippet line({ line, props, segments })}
    <div {...props}>
      {#each segments ?? [{ text: line.content, changed: false }] as segment}
        <span data-emphasis={segment.changed ? '' : undefined}>{segment.text}</span>
      {/each}
    </div>
  {/snippet}
</DiffViewer>
```

## Styling Hooks

The root exposes stable HPD-owned attributes:

```css
[data-hpd-diff-viewer] {
}

[data-hpd-diff-viewer][data-view-mode='unified'] {
}

[data-hpd-diff-viewer][data-view-mode='split'] {
}

[data-hpd-diff-viewer][data-variant='muted'] {
}

[data-hpd-diff-viewer][data-empty] {
}
```

Files, headers, and stats:

```css
[data-hpd-diff-file] {
}

[data-hpd-diff-header] {
}

[data-hpd-diff-file-badge] {
}

[data-hpd-diff-file-name] {
}

[data-hpd-diff-old-file-name] {
}

[data-hpd-diff-new-file-name] {
}

[data-hpd-diff-rename-arrow] {
}

[data-hpd-diff-stats] {
}

[data-hpd-diff-additions] {
}

[data-hpd-diff-deletions] {
}
```

Content, lines, folds, and split sides:

```css
[data-hpd-diff-content] {
}

[data-hpd-diff-line] {
}

[data-hpd-diff-line][data-line-type='add'] {
}

[data-hpd-diff-line][data-line-type='del'] {
}

[data-hpd-diff-line-number] {
}

[data-hpd-diff-indicator] {
}

[data-hpd-diff-line-content] {
}

[data-hpd-diff-fold] {
}

[data-hpd-diff-truncated] {
}

[data-hpd-diff-segment][data-changed] {
}

[data-hpd-diff-split-line] {
}

[data-hpd-diff-split-side][data-side='left'] {
}

[data-hpd-diff-split-side][data-side='right'] {
}
```
