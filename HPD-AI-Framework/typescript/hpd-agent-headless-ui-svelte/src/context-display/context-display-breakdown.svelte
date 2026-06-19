<svelte:options runes={true} />

<script lang="ts">
  import { getContextDisplayContext } from './context.js';
  import {
    createContextDisplayBreakdownElementProps,
    formatContextDisplayTokens,
    getContextDisplayBreakdownRows,
  } from './props.js';
  import type { ContextDisplayBreakdownProps } from './types.js';

  let {
    child,
    children,
    ...restProps
  }: ContextDisplayBreakdownProps = $props();

  const context = getContextDisplayContext();
  const model = $derived(context.getModel());
  const rows = $derived(getContextDisplayBreakdownRows(model));
  const elementProps = $derived(createContextDisplayBreakdownElementProps(model, restProps));
</script>

{#if child}
  {@render child({ model, props: elementProps, rows })}
{:else}
  <div {...elementProps}>
    {#if children}
      {@render children({ model, props: elementProps, rows })}
    {:else}
      {#each rows as row (row.key)}
        <div data-hpd-context-display-breakdown-row data-row-key={row.key}>
          <span>{row.label}</span>
          <span>{formatContextDisplayTokens(row.value)}</span>
        </div>
      {/each}
    {/if}
  </div>
{/if}

