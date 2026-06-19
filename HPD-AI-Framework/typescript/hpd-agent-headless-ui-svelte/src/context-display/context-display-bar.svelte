<svelte:options runes={true} />

<script lang="ts">
  import { getContextDisplayContext } from './context.js';
  import {
    createContextDisplayBarElementProps,
    createContextDisplayBarFillElementProps,
    formatContextDisplayPercent,
    formatContextDisplayTokens,
  } from './props.js';
  import type { ContextDisplayBarProps } from './types.js';

  let {
    child,
    children,
    ...restProps
  }: ContextDisplayBarProps = $props();

  const context = getContextDisplayContext();
  const model = $derived(context.getModel());
  const elementProps = $derived(createContextDisplayBarElementProps(model, restProps));
  const fillProps = $derived(createContextDisplayBarFillElementProps(model));
</script>

{#if child}
  {@render child({ fillProps, model, props: elementProps })}
{:else}
  <div {...elementProps}>
    {#if children}
      {@render children({ fillProps, model, props: elementProps })}
    {:else}
      <div {...fillProps}></div>
      <span data-hpd-context-display-bar-label>
        {formatContextDisplayTokens(model.totalTokens)}
        {#if model.modelContextWindow}
          / {formatContextDisplayTokens(model.modelContextWindow)}
        {/if}
        ({formatContextDisplayPercent(model.percent)})
      </span>
    {/if}
  </div>
{/if}

