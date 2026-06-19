<svelte:options runes={true} />

<script lang="ts">
  import { setDiffViewerContext } from './context.js';
  import DiffViewerFile from './diff-viewer-file.svelte';
  import {
    createDiffViewerElementProps,
    createDiffViewerModel,
  } from './props.js';
  import type {
    DiffViewerContext,
    DiffViewerElementProps,
    DiffViewerModel,
    DiffViewerProps,
  } from './types.js';

  let {
    child,
    children,
    contextLines,
    file,
    fold,
    header,
    line,
    maxLines,
    newFile,
    oldFile,
    patch,
    showHeader = true,
    showLineNumbers = true,
    showStats = true,
    size = 'default',
    splitLine,
    variant = 'default',
    viewMode = 'unified',
    ...restProps
  }: DiffViewerProps = $props();

  const model = $derived<DiffViewerModel>(createDiffViewerModel({
    newFile,
    oldFile,
    patch,
    size,
    variant,
    viewMode,
  }));
  const elementProps = $derived<DiffViewerElementProps>(
    createDiffViewerElementProps(model, restProps),
  );

  const diffContext = $state<DiffViewerContext>(createInitialContext());
  setDiffViewerContext(diffContext);

  $effect(() => {
    diffContext.contextLines = contextLines;
    diffContext.files = model.files;
    diffContext.maxLines = maxLines;
    diffContext.showLineNumbers = showLineNumbers;
    diffContext.viewMode = viewMode;
  });

  function createInitialContext(): DiffViewerContext {
    return {
      contextLines,
      files: model.files,
      maxLines,
      showLineNumbers,
      viewMode,
    };
  }
</script>

{#if child}
  {@render child({ model, props: elementProps })}
{:else}
  <div {...elementProps}>
    {#if children}
      {@render children({ model, props: elementProps })}
    {:else if model.files.length === 0}
      <div data-hpd-diff-empty>No diff content provided</div>
    {:else}
      {#each model.files as diffFile, fileIndex}
        <DiffViewerFile
          file={diffFile}
          {fileIndex}
          {showHeader}
          {showStats}
          fileSnippet={file}
          headerSnippet={header}
          lineSnippet={line}
          foldSnippet={fold}
          splitLineSnippet={splitLine}
        />
      {/each}
    {/if}
  </div>
{/if}
