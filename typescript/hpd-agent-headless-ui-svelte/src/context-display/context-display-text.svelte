<svelte:options runes={true} />

<script lang="ts">
  import { getContextDisplayContext } from './context.js';
  import {
    createContextDisplayTextElementProps,
    formatContextDisplayPercent,
    formatContextDisplayTokens,
  } from './props.js';
  import type { ContextDisplayTextProps } from './types.js';

  let {
    child,
    children,
    ...restProps
  }: ContextDisplayTextProps = $props();

  const context = getContextDisplayContext();
  const model = $derived(context.getModel());
  const elementProps = $derived(createContextDisplayTextElementProps(model, restProps));
</script>

{#if child}
  {@render child({ model, props: elementProps })}
{:else}
  <span {...elementProps}>
    {#if children}
      {@render children({ model, props: elementProps })}
    {:else}
      {formatContextDisplayTokens(model.totalTokens)}
      {#if model.modelContextWindow}
        / {formatContextDisplayTokens(model.modelContextWindow)}
      {/if}
      <span data-hpd-context-display-text-percent>{formatContextDisplayPercent(model.percent)}</span>
    {/if}
  </span>
{/if}

