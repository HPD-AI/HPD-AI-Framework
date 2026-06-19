<svelte:options runes={true} />

<script lang="ts">
  import { createThreadStatusIndicatorElementProps } from './props.js';
  import type { ThreadStatusIndicatorProps } from './types.js';

  let {
    child,
    children,
    status,
    ...restProps
  }: ThreadStatusIndicatorProps = $props();

  const elementProps = $derived(createThreadStatusIndicatorElementProps(status, restProps));
</script>

{#if child}
  {@render child({ props: elementProps, status })}
{:else}
  <span {...elementProps}>
    {#if children}
      {@render children({ status })}
    {:else}
      {status.label}
    {/if}
  </span>
{/if}
