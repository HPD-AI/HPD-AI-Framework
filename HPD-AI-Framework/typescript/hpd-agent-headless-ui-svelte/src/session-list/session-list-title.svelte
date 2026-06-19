<svelte:options runes={true} />

<script lang="ts">
  import { getSessionListItemContext } from './context.js';
  import { createSessionListTitleElementProps } from './props.js';
  import type { SessionListTitleProps } from './types.js';

  let {
    children,
    ...restProps
  }: SessionListTitleProps = $props();

  const context = getSessionListItemContext();
  const elementProps = $derived(createSessionListTitleElementProps(context.item, restProps));
</script>

<span {...elementProps}>
  {#if children}
    {@render children({ item: context.item, props: elementProps })}
  {:else}
    {context.item.label}
  {/if}
</span>
