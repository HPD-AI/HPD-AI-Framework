<svelte:options runes={true} />

<script lang="ts">
  import { getDiffViewerContext } from './context.js';
  import DiffViewerStats from './diff-viewer-stats.svelte';
  import {
    createDiffViewerHeaderChildProps,
    getDiffFileExtension,
  } from './props.js';
  import type {
    DiffViewerHeaderChildProps,
    DiffViewerHeaderProps,
  } from './types.js';
  import type { DiffFile } from '@hpd-research/hpd-agent-headless-ui';

  let {
    children,
    file,
    fileIndex = 0,
    showStats = true,
    ...restProps
  }: DiffViewerHeaderProps = $props();

  const context = getDiffViewerContext();
  const resolvedFile = $derived<DiffFile | undefined>(file ?? context.files[fileIndex]);
  const childProps = $derived<DiffViewerHeaderChildProps | null>(
    resolvedFile ? createDiffViewerHeaderChildProps(resolvedFile, fileIndex, restProps) : null,
  );
  const extension = $derived(getDiffFileExtension(childProps?.displayName));
</script>

{#if resolvedFile && childProps && childProps.displayName}
  <div {...childProps.props}>
    {#if children}
      {@render children(childProps)}
    {:else}
      {#if extension}
        <span data-hpd-diff-file-badge>{extension}</span>
      {/if}

      <span data-hpd-diff-file-name>
        {#if childProps.renamed}
          <span data-hpd-diff-old-file-name>{childProps.oldName}</span>
          <span data-hpd-diff-rename-arrow aria-hidden="true">-&gt;</span>
          <span data-hpd-diff-new-file-name>{childProps.newName}</span>
        {:else}
          {childProps.displayName}
        {/if}
      </span>

      {#if showStats && (childProps.additions > 0 || childProps.deletions > 0)}
        <DiffViewerStats file={resolvedFile} {fileIndex} />
      {/if}
    {/if}
  </div>
{/if}
