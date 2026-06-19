<svelte:options runes={true} />

<script lang="ts">
  import { getComposerQuoteContext } from './context.js';
  import { createComposerQuoteTextElementProps } from './props.js';
  import type { ComposerQuoteTextProps } from './types.js';

  let {
    children,
    ...restProps
  }: ComposerQuoteTextProps = $props();

  const context = getComposerQuoteContext();
  const elementProps = $derived(createComposerQuoteTextElementProps(restProps));
</script>

{#if context.quote}
  <span {...elementProps}>
    {#if children}
      {@render children({ props: elementProps, quote: context.quote })}
    {:else}
      {context.quote.text}
    {/if}
  </span>
{/if}
