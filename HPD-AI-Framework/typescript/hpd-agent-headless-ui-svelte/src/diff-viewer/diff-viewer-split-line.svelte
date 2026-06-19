<svelte:options runes={true} />

<script lang="ts">
  import {
    createDiffViewerSplitLineChildProps,
    createDiffViewerSplitSideElementProps,
    getDiffLineIndicator,
    getDiffLineNumber,
  } from './props.js';
  import type {
    DiffViewerSplitLineChildProps,
    DiffViewerSplitLineProps,
  } from './types.js';
  import type { DiffLine } from '@hpd-research/hpd-agent-headless-ui';

  let {
    children,
    file,
    fileIndex = 0,
    index = 0,
    pair,
    showLineNumbers = true,
    ...restProps
  }: DiffViewerSplitLineProps = $props();

  const childProps = $derived<DiffViewerSplitLineChildProps>(createDiffViewerSplitLineChildProps({
    file: file ?? { lines: [], additions: 0, deletions: 0 },
    fileIndex,
    index,
    pair,
    restProps,
  }));

  function content(line: DiffLine | null): string {
    return line?.content ?? '';
  }
</script>

<div {...childProps.props}>
  {#if children}
    {@render children(childProps)}
  {:else}
    <div {...createDiffViewerSplitSideElementProps('left', pair.left)}>
      {#if showLineNumbers}
        <span data-hpd-diff-line-number>{pair.left ? getDiffLineNumber(pair.left, 'left') ?? '' : ''}</span>
      {/if}
      <span data-hpd-diff-indicator>{getDiffLineIndicator(pair.left, 'left')}</span>
      <span data-hpd-diff-line-content>{content(pair.left)}</span>
    </div>

    <div {...createDiffViewerSplitSideElementProps('right', pair.right)}>
      {#if showLineNumbers}
        <span data-hpd-diff-line-number>{pair.right ? getDiffLineNumber(pair.right, 'right') ?? '' : ''}</span>
      {/if}
      <span data-hpd-diff-indicator>{getDiffLineIndicator(pair.right, 'right')}</span>
      <span data-hpd-diff-line-content>{content(pair.right)}</span>
    </div>
  {/if}
</div>
