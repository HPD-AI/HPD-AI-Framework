<svelte:options runes={true} />

<script lang="ts">
  import { getDiffViewerContext } from './context.js';
  import { createDiffViewerStatsChildProps } from './props.js';
  import type {
    DiffViewerStatsChildProps,
    DiffViewerStatsProps,
  } from './types.js';
  import type { DiffFile } from '@hpd-research/hpd-agent-headless-ui';

  let {
    children,
    file,
    fileIndex = 0,
    ...restProps
  }: DiffViewerStatsProps = $props();

  const context = getDiffViewerContext();
  const resolvedFile = $derived<DiffFile | undefined>(file ?? context.files[fileIndex]);
  const childProps = $derived<DiffViewerStatsChildProps | null>(
    resolvedFile ? createDiffViewerStatsChildProps(resolvedFile, fileIndex, restProps) : null,
  );
</script>

{#if resolvedFile && childProps}
  <span {...childProps.props}>
    {#if children}
      {@render children(childProps)}
    {:else}
      <span data-hpd-diff-additions>+{childProps.additions}</span>
      <span data-hpd-diff-deletions>-{childProps.deletions}</span>
    {/if}
  </span>
{/if}
