<svelte:options runes={true} />

<script lang="ts">
  import {
    createDiffViewerLineChildProps,
    createDiffViewerSegmentElementProps,
    getDiffLineIndicator,
    getDiffLineNumber,
  } from './props.js';
  import type {
    DiffViewerLineChildProps,
    DiffViewerLineProps,
  } from './types.js';

  let {
    children,
    file,
    fileIndex = 0,
    index = 0,
    line,
    segments = null,
    showLineNumbers = true,
    ...restProps
  }: DiffViewerLineProps = $props();

  const childProps = $derived<DiffViewerLineChildProps>(createDiffViewerLineChildProps({
    file: file ?? { lines: [], additions: 0, deletions: 0 },
    fileIndex,
    index,
    line,
    restProps,
    segments,
  }));
  const lineNumber = $derived(getDiffLineNumber(line));
  const indicator = $derived(getDiffLineIndicator(line));
</script>

<div {...childProps.props}>
  {#if children}
    {@render children(childProps)}
  {:else}
    {#if showLineNumbers}
      <span data-hpd-diff-line-number>{lineNumber ?? ''}</span>
    {/if}

    <span data-hpd-diff-indicator>{indicator}</span>

    <span data-hpd-diff-line-content>
      {#if segments && segments.length > 0 && line.type !== 'normal'}
        {#each segments as segment}
          <span {...createDiffViewerSegmentElementProps(segment)}>{segment.text}</span>
        {/each}
      {:else}
        {line.content}
      {/if}
    </span>
  {/if}
</div>
