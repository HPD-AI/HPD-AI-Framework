<svelte:options runes={true} />

<script lang="ts">
  import { getDiffViewerContext } from './context.js';
  import DiffViewerLine from './diff-viewer-line.svelte';
  import DiffViewerSplitLine from './diff-viewer-split-line.svelte';
  import {
    createDiffViewerContentChildProps,
    createDiffViewerFoldChildProps,
    createDiffViewerSegmentMap,
  } from './props.js';
  import type {
    DiffViewerContentChildProps,
    DiffViewerContentProps,
    DiffViewerFoldChildProps,
  } from './types.js';
  import type { DiffFile } from '@hpd-research/hpd-agent-headless-ui';

  let {
    children,
    file,
    fileIndex = 0,
    fold,
    line,
    splitLine,
    ...restProps
  }: DiffViewerContentProps = $props();

  const context = getDiffViewerContext();
  const resolvedFile = $derived<DiffFile | undefined>(file ?? context.files[fileIndex]);
  const childProps = $derived<DiffViewerContentChildProps | null>(
    resolvedFile
      ? createDiffViewerContentChildProps({
        contextLines: context.contextLines,
        file: resolvedFile,
        fileIndex,
        maxLines: context.maxLines,
        restProps,
      })
      : null,
  );
  const segmentMap = $derived(resolvedFile ? createDiffViewerSegmentMap(resolvedFile.lines) : new Map());
</script>

{#if resolvedFile && childProps}
  <div {...childProps.props}>
    {#if children}
      {@render children(childProps)}
    {:else if context.viewMode === 'split'}
      {#each childProps.splitPairs as pair, index}
        <DiffViewerSplitLine
          file={resolvedFile}
          {fileIndex}
          {index}
          {pair}
          showLineNumbers={context.showLineNumbers}
          children={splitLine}
        />
      {/each}
    {:else}
      {#each childProps.displayLines as displayLine, index}
        {#if displayLine.type === 'fold'}
          {@const foldProps: DiffViewerFoldChildProps = createDiffViewerFoldChildProps({
            file: resolvedFile,
            fileIndex,
            fold: displayLine,
            index,
          })}
          {#if fold}
            {@render fold(foldProps)}
          {:else}
            <div {...foldProps.props}>--- {displayLine.hiddenCount} lines hidden ---</div>
          {/if}
        {:else}
          <DiffViewerLine
            file={resolvedFile}
            {fileIndex}
            {index}
            line={displayLine}
            segments={segmentMap.get(displayLine) ?? null}
            showLineNumbers={context.showLineNumbers}
            children={line}
          />
        {/if}
      {/each}
    {/if}

    {#if childProps.truncated}
      <div data-hpd-diff-truncated>... ({childProps.remainingCount} more lines)</div>
    {/if}
  </div>
{/if}
