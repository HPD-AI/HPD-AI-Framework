<svelte:options runes={true} />

<script lang="ts">
  import ThreadWorkParts from './thread-work-parts.svelte';
  import {
    createThreadWorkGroupElementProps,
    getVisibleThreadWorkParts,
  } from './props.js';
  import type { ThreadWorkGroupProps } from './types.js';

  let {
    work,
    child,
    children,
    showFinalDraft = false,
    workPart,
    ...restProps
  }: ThreadWorkGroupProps = $props();

  const elementProps = $derived(createThreadWorkGroupElementProps(work, restProps));
  const parts = $derived(getVisibleThreadWorkParts(work, showFinalDraft));
</script>

{#if child}
  {@render child({ props: elementProps, work, parts, status: work.status })}
{:else}
  <details {...elementProps}>
    {#if children}
      {@render children({ work, parts, status: work.status })}
    {:else}
      <summary data-hpd-thread-work-summary>
        <span>{work.label}</span>
        <span data-hpd-thread-work-state>{work.status}</span>
      </summary>

      {#if work.error}
        <div data-hpd-thread-work-error>{work.error}</div>
      {/if}

      <ThreadWorkParts {work} {showFinalDraft} {workPart} />
    {/if}
  </details>
{/if}
