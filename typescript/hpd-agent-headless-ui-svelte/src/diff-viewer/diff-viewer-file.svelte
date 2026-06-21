<svelte:options runes={true} />

<script lang="ts">
  import { getDiffViewerContext } from './context.js';
  import DiffViewerContent from './diff-viewer-content.svelte';
  import DiffViewerHeader from './diff-viewer-header.svelte';
  import { createDiffViewerFileChildProps } from './props.js';
  import type {
    DiffViewerFileChildProps,
    DiffViewerFileProps,
    DiffViewerFoldChildProps,
    DiffViewerHeaderChildProps,
    DiffViewerLineChildProps,
    DiffViewerSplitLineChildProps,
  } from './types.js';
  import type { DiffFile } from '@hpd-research/hpd-agent-headless-ui';
  import type { Snippet } from 'svelte';

  let {
    children,
    file,
    fileIndex = 0,
    fileSnippet,
    foldSnippet,
    headerSnippet,
    lineSnippet,
    showHeader = true,
    showStats = true,
    splitLineSnippet,
    ...restProps
  }: DiffViewerFileProps & {
    fileSnippet?: Snippet<[DiffViewerFileChildProps]>;
    foldSnippet?: Snippet<[DiffViewerFoldChildProps]>;
    headerSnippet?: Snippet<[DiffViewerHeaderChildProps]>;
    lineSnippet?: Snippet<[DiffViewerLineChildProps]>;
    showHeader?: boolean;
    showStats?: boolean;
    splitLineSnippet?: Snippet<[DiffViewerSplitLineChildProps]>;
  } = $props();

  const context = getDiffViewerContext();
  const resolvedFile = $derived<DiffFile | undefined>(file ?? context.files[fileIndex]);
  const childProps = $derived<DiffViewerFileChildProps | null>(
    resolvedFile ? createDiffViewerFileChildProps(resolvedFile, fileIndex, restProps) : null,
  );
</script>

{#if resolvedFile && childProps}
  {#if fileSnippet}
    {@render fileSnippet(childProps)}
  {:else}
    <div {...childProps.props}>
      {#if children}
        {@render children(childProps)}
      {:else}
        {#if showHeader}
          <DiffViewerHeader
            file={resolvedFile}
            {fileIndex}
            {showStats}
            children={headerSnippet}
          />
        {/if}

        <DiffViewerContent
          file={resolvedFile}
          {fileIndex}
          line={lineSnippet}
          fold={foldSnippet}
          splitLine={splitLineSnippet}
        />
      {/if}
    </div>
  {/if}
{/if}
