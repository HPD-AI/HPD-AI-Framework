# DiffViewer Proposal

`DiffViewer` renders source diffs from either a unified patch string or an
old/new file pair.

## Ownership

Diff parsing and line shaping belong in the framework-neutral headless core.
Rendering, snippets, layout, and styling hooks belong in the Svelte adapter.

This keeps the primitive useful in several places:

- markdown code blocks with `diff` language
- tool calls that return patches
- revision previews
- app-specific file change panels

## Shape

```text
src/diff-viewer/
  diff-viewer.svelte
  diff-viewer-file.svelte
  diff-viewer-header.svelte
  diff-viewer-stats.svelte
  diff-viewer-content.svelte
  diff-viewer-line.svelte
  diff-viewer-split-line.svelte
  context.ts
  props.ts
  types.ts
  index.ts
```

## API

```svelte
<DiffViewer patch={patchText} />
<DiffViewer oldFile={{ name: 'a.ts', content: oldText }} newFile={{ name: 'a.ts', content: newText }} />
```

Optional display policy:

```svelte
<DiffViewer
  patch={patchText}
  viewMode="split"
  contextLines={3}
  maxLines={120}
/>
```

Custom rendering stays snippet-based:

```svelte
<DiffViewer patch={patchText}>
  {#snippet line({ line, props, segments })}
    <div {...props}>
      {#each segments ?? [{ text: line.content, changed: false }] as segment}
        <span data-changed={segment.changed ? '' : undefined}>{segment.text}</span>
      {/each}
    </div>
  {/snippet}
</DiffViewer>
```

## Boundaries

`DiffViewer` should not fetch files, apply patches, or mutate thread state. It
is a read/render primitive over already-projected content.

Tool-specific behavior belongs in `ToolCall` snippets. Markdown-specific
routing belongs in `MarkdownText` code snippets.
