<svelte:options runes={true} />

<script lang="ts">
  import { getSessionListItemContext } from './context.js';
  import { createSessionListSubtitleElementProps } from './props.js';
  import type { SessionListSubtitleProps } from './types.js';

  let {
    children,
    ...restProps
  }: SessionListSubtitleProps = $props();

  const context = getSessionListItemContext();
  const elementProps = $derived(createSessionListSubtitleElementProps(context.item, restProps));
</script>

{#if children}
  <span {...elementProps}>
    {@render children({ item: context.item, props: elementProps })}
  </span>
{:else if context.item.subtitle}
  <span {...elementProps}>{context.item.subtitle}</span>
{/if}
